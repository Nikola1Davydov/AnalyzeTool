using System.IO;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Versions;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Sdk;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Extensions.Loading;

/// <summary>What happened to an extension at its last load.</summary>
/// <param name="Descriptor">The folder as found.</param>
/// <param name="Enabled">The user has not turned it off.</param>
/// <param name="Commands">The names of its commands now in the pipeline.</param>
/// <param name="Errors">Why something did not load: a missing build, an incompatible Sdk, a type that cannot be created.</param>
/// <param name="Diagnostics">The compiler's errors and warnings, when the sources were compiled.</param>
/// <param name="LoadedVersion">The pinned version that is loaded; <see langword="null" /> for the current files.</param>
/// <param name="UsesOlderBuild">The current sources do not compile, and an earlier build is loaded instead.</param>
public sealed record ExtensionState(
    ExtensionDescriptor Descriptor,
    bool Enabled,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CompileDiagnostic> Diagnostics,
    string? LoadedVersion,
    bool UsesOlderBuild);

/// <summary>
///     Loads the extensions into the pipeline and keeps them current: compiles the sources of source extensions, loads the
///     DLLs of DLL extensions, registers their commands as a group per extension, and replaces a group whenever its folder
///     changes — no restart of Revit, no Reload button needed.
/// </summary>
/// <remarks>
///     One bad extension never stops the others: its error lands in <see cref="ExtensionState.Errors" />, which the pages and
///     AI clients show. A command still running in an old load context keeps it alive until it returns.
/// </remarks>
public sealed partial class ExtensionHost : IDisposable
{
    private static readonly string[] SourceExtensions = [".cs"];

    private readonly ExtensionCatalog _catalog;
    private readonly ExtensionStateStore _store;
    private readonly IExtensionHistory _history;
    private readonly CommandRegistry _registry;
    private readonly ILogger<ExtensionHost> _logger;
    private readonly Version _sdkVersion = typeof(IRevitTask).Assembly.GetName().Version ?? new Version(1, 0);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _folderProblems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the host.</summary>
    public ExtensionHost(ExtensionCatalog catalog, ExtensionStateStore store, IExtensionHistory history, CommandRegistry registry, ILogger<ExtensionHost> logger)
    {
        _catalog = catalog;
        _store = store;
        _history = history;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>Raised after extensions were loaded, reloaded or removed: the ribbon and the pages refresh.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the Revit year the extensions are loaded for.</summary>
    public string RevitVersion => _catalog.RevitVersion;

    /// <summary>Gets the state of every extension found at the last load, by id.</summary>
    public IReadOnlyList<ExtensionState> GetStates()
    {
        lock (_loaded)
        {
            return _loaded.Values.Select(loaded => loaded.State).OrderBy(state => state.Descriptor.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Gets the state of one extension; <see langword="null" /> for an unknown id.</summary>
    public ExtensionState? GetState(string id)
    {
        lock (_loaded)
        {
            return _loaded.GetValueOrDefault(id)?.State;
        }
    }

    /// <summary>Gets the folders that hold a <c>plugin.json</c> that cannot be read: folder → reason.</summary>
    public IReadOnlyDictionary<string, string> GetFolderProblems()
    {
        lock (_loaded)
        {
            return new Dictionary<string, string>(_folderProblems, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Scans every root and loads every extension again; extensions that are gone leave the pipeline.</summary>
    public async Task ReloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var found = _catalog.Scan(problems);
            var duplicates = found.GroupBy(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToList();
            foreach (var group in duplicates)
            {
                foreach (var extra in group.Skip(1))
                {
                    problems[extra.Directory] = $"The id '{group.Key}' is used by {group.First().Directory} already; this folder is skipped.";
                }
            }

            var unique = found.GroupBy(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
            foreach (var gone in Ids().Except(unique.Select(descriptor => descriptor.Id), StringComparer.OrdinalIgnoreCase).ToList())
            {
                Unload(gone);
            }

            foreach (var descriptor in unique)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadOne(descriptor);
            }

            lock (_loaded)
            {
                _folderProblems.Clear();
                foreach (var (folder, reason) in problems)
                {
                    _folderProblems[folder] = reason;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads one extension again — after its files changed, a version was restored or pinned, or it was turned on or off.</summary>
    /// <returns>The new state; <see langword="null" /> when the folder is no longer an extension (it left the pipeline).</returns>
    public async Task<ExtensionState?> ReloadAsync(string id, CancellationToken cancellationToken = default)
    {
        ExtensionState? state;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = GetState(id)?.Descriptor;
            var descriptor = directory is null ? _catalog.Find(id) : _catalog.Read(directory.Directory, directory.Zone, directory.IsLegacyLayout);
            if (descriptor is null || !string.Equals(descriptor.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                Unload(id);
                state = null;
                // The folder may now carry another id: the next full scan picks it up
                if (descriptor is not null)
                {
                    LoadOne(descriptor);
                }
            }
            else
            {
                state = LoadOne(descriptor);
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return state;
    }

    /// <summary>Drops an extension from the pipeline and the list — before its folder is deleted.</summary>
    public async Task UnloadAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Unload(id);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Compiles the sources of a source extension without loading them: the errors for the editor.</summary>
    public ExtensionBuild Check(ExtensionDescriptor descriptor)
    {
        return ExtensionBuilder.Build(descriptor, RevitVersion);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    private IEnumerable<string> Ids()
    {
        lock (_loaded)
        {
            return _loaded.Keys.ToList();
        }
    }

    private ExtensionState LoadOne(ExtensionDescriptor descriptor)
    {
        var id = descriptor.Id;
        var enabled = _store.IsEnabled(id);
        var errors = new List<string>();
        IReadOnlyList<CompileDiagnostic> diagnostics = [];
        string? loadedVersion = null;
        var usesOlderBuild = false;
        ExtensionLoadContext? context = null;
        var commands = new List<CommandDescriptor>();

        try
        {
            if (enabled)
            {
                string? assemblyPath = null;
                switch (descriptor.Kind)
                {
                    case ExtensionKind.Source:
                    {
                        var build = BuildSources(descriptor, errors);
                        diagnostics = build.Diagnostics;
                        loadedVersion = build.Version;
                        usesOlderBuild = build.Failed && build.AssemblyPath is not null;
                        assemblyPath = build.AssemblyPath;
                        if (build.Failed)
                        {
                            errors.Add(build.AssemblyPath is null
                                ? "The sources do not compile; nothing is loaded."
                                : "The sources do not compile; the previous build is still in use.");
                        }

                        break;
                    }
                    case ExtensionKind.Dll when descriptor.EntryAssemblyPath is null:
                        errors.Add($"No build for Revit {RevitVersion}: '{descriptor.Manifest.EntryAssembly}' is not in '{RevitVersion}\\' or the extension folder.");
                        break;
                    case ExtensionKind.Dll:
                        assemblyPath = descriptor.EntryAssemblyPath;
                        break;
                }

                if (assemblyPath is not null)
                {
                    context = new ExtensionLoadContext(id, descriptor.Kind == ExtensionKind.Dll ? assemblyPath : null);
                    context.ProbingFolders.Add(Path.Combine(descriptor.Directory, ExtensionBuilder.LibraryFolder));
                    var assembly = context.LoadFromFileBytes(assemblyPath);
                    if (CheckSdk(assembly, errors))
                    {
                        commands.AddRange(SdkCommands.Describe(assembly, id, errors));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(exception.Message);
            LogLoadFailed(_logger, exception, id);
        }

        errors.AddRange(_registry.SetGroup(id, commands));
        var registered = _registry.GetGroup(id).Select(command => command.Name).ToList();
        if (registered.Count == 0 && context is not null)
        {
            context.Unload();
            context = null;
        }

        var state = new ExtensionState(descriptor, enabled, registered, errors, diagnostics, loadedVersion, usesOlderBuild);
        Loaded? previous;
        lock (_loaded)
        {
            previous = _loaded.GetValueOrDefault(id);
            _loaded[id] = new Loaded(state, context);
        }

        previous?.Unload();
        if (errors.Count > 0)
        {
            LogLoadedWithErrors(_logger, id, string.Join(" | ", errors));
        }
        else if (enabled)
        {
            LogLoaded(_logger, id, descriptor.Kind, registered.Count);
        }

        return state;
    }

    /// <summary>Builds the current files, or the pinned version when the extension is pinned and the version exists.</summary>
    private ExtensionBuild BuildSources(ExtensionDescriptor descriptor, List<string> errors)
    {
        var pinned = _store.GetPinnedVersion(descriptor.Id);
        if (pinned is not null)
        {
            try
            {
                var files = _history.GetFiles(descriptor.Directory, pinned, SourceExtensions)
                    .Where(file => file.Key.StartsWith(ExtensionCatalog.SourceFolder + "/", StringComparison.OrdinalIgnoreCase))
                    .Select(file => (Path: file.Key.Replace('/', Path.DirectorySeparatorChar), Text: file.Value))
                    .ToList();
                return ExtensionBuilder.Build(descriptor, RevitVersion, files, pinned);
            }
            catch (CommandException exception)
            {
                errors.Add($"The pinned version {pinned[..Math.Min(7, pinned.Length)]} cannot be loaded ({exception.Message}); the current files are loaded instead.");
            }
        }

        return ExtensionBuilder.Build(descriptor, RevitVersion);
    }

    /// <summary>The Sdk an assembly was built against must be of the same major as this AnalyseTool's, and not newer.</summary>
    private bool CheckSdk(System.Reflection.Assembly assembly, List<string> errors)
    {
        var referenced = assembly.GetReferencedAssemblies().FirstOrDefault(name => string.Equals(name.Name, "AnalyseTool.Sdk", StringComparison.OrdinalIgnoreCase))?.Version;
        if (referenced is null)
        {
            errors.Add("The assembly does not reference AnalyseTool.Sdk: it has no commands for AnalyseTool.");
            return false;
        }

        if (referenced.Major != _sdkVersion.Major)
        {
            errors.Add($"Built against AnalyseTool.Sdk {referenced}, incompatible with the Sdk {_sdkVersion.Major}.x of this AnalyseTool.");
            return false;
        }

        if (referenced > _sdkVersion)
        {
            errors.Add($"Built against AnalyseTool.Sdk {referenced}, newer than the Sdk {_sdkVersion} of this AnalyseTool: update AnalyseTool.");
            return false;
        }

        return true;
    }

    private void Unload(string id)
    {
        _registry.RemoveGroup(id);
        Loaded? previous;
        lock (_loaded)
        {
            _loaded.Remove(id, out previous);
        }

        previous?.Unload();
    }

    [LoggerMessage(LogLevel.Information, "Extension {Id} loaded ({Kind}): {Count} commands")]
    private static partial void LogLoaded(ILogger logger, string id, ExtensionKind kind, int count);

    [LoggerMessage(LogLevel.Warning, "Extension {Id}: {Errors}")]
    private static partial void LogLoadedWithErrors(ILogger logger, string id, string errors);

    [LoggerMessage(LogLevel.Error, "Extension {Id} failed to load")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception, string id);

    private sealed record Loaded(ExtensionState State, ExtensionLoadContext? Context)
    {
        public void Unload()
        {
            try
            {
                Context?.Unload();
            }
            catch (InvalidOperationException)
            {
                // Already unloading
            }
        }
    }
}
