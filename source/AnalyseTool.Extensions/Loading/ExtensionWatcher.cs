using System.Collections.Concurrent;
using System.IO;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Extensions.Loading;

/// <summary>
///     Loads the extensions when the add-in starts and reloads an extension whenever its files change: save a <c>.cs</c> in
///     any editor, or let an AI client write it, and the button runs the new code a moment later.
/// </summary>
/// <remarks>
///     Changes under <c>bin\</c>, <c>obj\</c> and <c>.git\</c> are the host's and git's own and never trigger a load. Bursts of
///     changes (an editor saving several files, a restore) become one reload per extension after a short quiet period.
/// </remarks>
public sealed partial class ExtensionWatcher(ExtensionHost host, ExtensionPaths paths, ILogger<ExtensionWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);
    private static readonly string[] Ignored = ["bin", "obj", ".git", ".vs", "node_modules"];

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationToken _stopping;

    /// <summary>Gets a task that completes once the first load of every extension is done.</summary>
    public Task FirstLoad => _firstLoad.Task;

    private readonly TaskCompletionSource _firstLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Watches the roots again — after the user added or removed a folder of extensions.</summary>
    public void RefreshRoots()
    {
        lock (_watchers)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            foreach (var root in paths.GetRoots())
            {
                try
                {
                    Directory.CreateDirectory(root.Path);
                    var watcher = new FileSystemWatcher(root.Path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 64 * 1024
                    };
                    var rootPath = root.Path;
                    watcher.Changed += (_, e) => OnChanged(rootPath, e.FullPath);
                    watcher.Created += (_, e) => OnChanged(rootPath, e.FullPath);
                    watcher.Deleted += (_, e) => OnChanged(rootPath, e.FullPath);
                    watcher.Renamed += (_, e) =>
                    {
                        OnChanged(rootPath, e.OldFullPath);
                        OnChanged(rootPath, e.FullPath);
                    };
                    watcher.Error += (_, e) => LogWatcherFailed(logger, e.GetException(), rootPath);
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    LogWatcherFailed(logger, exception, root.Path);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        // Off the startup path of Revit: compiling sources must not hold up the ribbon
        await Task.Yield();
        try
        {
            await host.ReloadAllAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFirstLoadFailed(logger, exception);
        }
        finally
        {
            _firstLoad.TrySetResult();
        }

        RefreshRoots();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        lock (_watchers)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        base.Dispose();
    }

    private void OnChanged(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length == 0 || parts[0] is "." or ".." || parts[0].StartsWith('.'))
        {
            return;
        }

        // The 1.x layout <root>\<year>\<id>: the extension folder is one level deeper
        var depth = parts[0].Length == 4 && parts[0].All(char.IsDigit) ? 2 : 1;
        // A file lying in the root itself, or the year folder of the 1.x layout: no extension folder
        if (parts.Length < depth || (parts.Length == depth && File.Exists(fullPath)))
        {
            return;
        }

        if (parts.Skip(depth).Any(part => Ignored.Contains(part, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        // A tab: a change in a button folder reloads that button; any other (its _shared\ C#, a bundle.json, a new folder) all
        if (parts[0].EndsWith(ButtonFolder.TabSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var button = Array.FindIndex(parts, part => part.EndsWith(ButtonFolder.ButtonSuffix, StringComparison.OrdinalIgnoreCase));
            depth = button > 0 && button < parts.Length - 1 ? button + 1 : 1;
        }

        var folder = Path.Combine([root, .. parts.Take(depth)]);
        Schedule(folder);
    }

    private void Schedule(string folder)
    {
        var source = new CancellationTokenSource();
        var previous = _pending.AddOrUpdate(folder, source, (_, old) =>
        {
            old.Cancel();
            return source;
        });

        _ = ReloadLaterAsync(folder, source);
    }

    private async Task ReloadLaterAsync(string folder, CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(Quiet, source.Token).ConfigureAwait(false);
            await FirstLoad.ConfigureAwait(false);
            _pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(folder, source));
            var id = host.GetStates().FirstOrDefault(state => ExtensionPaths.SamePath(state.Descriptor.Directory, folder))?.Descriptor.Id;
            if (id is not null)
            {
                await host.ReloadAsync(id, _stopping).ConfigureAwait(false);
            }
            else if (File.Exists(Path.Combine(folder, ExtensionManifest.FileName)) || !Directory.Exists(folder) || ButtonFolder.IsTab(folder) || ButtonFolder.IsButton(folder))
            {
                // A new folder, or one whose plugin.json was broken before: a full scan finds it
                await host.ReloadAllAsync(_stopping).ConfigureAwait(false);
            }

            LogReloaded(logger, folder);
        }
        catch (OperationCanceledException)
        {
            // A newer change of the same folder took over, or the add-in stops
        }
        catch (Exception exception)
        {
            LogReloadFailed(logger, exception, folder);
        }
        finally
        {
            source.Dispose();
        }
    }

    [LoggerMessage(LogLevel.Debug, "Extension folder {Folder} changed and was reloaded")]
    private static partial void LogReloaded(ILogger logger, string folder);

    [LoggerMessage(LogLevel.Warning, "Extension folder {Folder} changed but could not be reloaded")]
    private static partial void LogReloadFailed(ILogger logger, Exception exception, string folder);

    [LoggerMessage(LogLevel.Warning, "The folder {Root} of extensions cannot be watched: changes need a manual reload")]
    private static partial void LogWatcherFailed(ILogger logger, Exception? exception, string root);

    [LoggerMessage(LogLevel.Error, "The extensions could not be loaded")]
    private static partial void LogFirstLoadFailed(ILogger logger, Exception exception);
}
