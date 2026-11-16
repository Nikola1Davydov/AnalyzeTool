using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Versions;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Extensions;

/// <summary>
///     What the pages and AI clients do with extensions: create, edit, version, pin, turn off, remove. The commands in
///     <c>Features/</c> are thin wrappers over it.
/// </summary>
public sealed class ExtensionManager(ExtensionHost host, ExtensionCatalog catalog, ExtensionPaths paths, ExtensionStateStore store, IExtensionHistory history)
{
    private const long MaxReadBytes = 2 * 1024 * 1024;

    /// <summary>Gets the user saving versions: the Windows user.</summary>
    public static string Author => Environment.UserName;

    /// <summary>Gets the host the extensions are loaded by.</summary>
    public ExtensionHost Host => host;

    /// <summary>Gets the paths of the profile.</summary>
    public ExtensionPaths Paths => paths;

    /// <summary>Gets the history service.</summary>
    public IExtensionHistory History => history;

    /// <summary>Finds a loaded extension or throws <see cref="ExtensionErrorCodes.ExtensionNotFound" />.</summary>
    public ExtensionState Get(string id)
    {
        return host.GetState(id) ?? throw new CommandException(ExtensionErrorCodes.ExtensionNotFound, $"There is no extension '{id}'.");
    }

    /// <summary>Creates a new extension in the authoring root (or <paramref name="root" />), starts its history and loads it.</summary>
    public async Task<ExtensionState> CreateAsync(string name, string? id, string? description, ExtensionTemplate template, string? panel, string? root, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        id = string.IsNullOrWhiteSpace(id) ? ExtensionScaffold.ToId(name) : id.Trim();
        if (!ExtensionScaffold.IsValidId(id))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is not a valid id: use letters, digits, '.', '-' and '_'.");
        }

        if (host.GetState(id) is not null || catalog.Find(id) is not null)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"An extension with the id '{id}' exists already.");
        }

        var target = root is null ? paths.AuthoringRoot : paths.GetRoots().FirstOrDefault(candidate => ExtensionPaths.SamePath(candidate.Path, root) && candidate.Zone == ExtensionZone.Dev)?.Path
                                                         ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"'{root}' is not one of your folders of extensions.");
        var directory = Path.Combine(target, id);
        if (Directory.Exists(directory))
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The folder {directory} exists already.");
        }

        ExtensionScaffold.Create(directory, id, name.Trim(), description, template, panel, catalog.RevitVersion);
        if (history.IsAvailable)
        {
            history.Init(directory, Author, $"Created «{name.Trim()}»", ExtensionScaffold.SourceIgnore);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(id);
    }

    /// <summary>
    ///     Removes an extension: it leaves the pipeline, and its folder moves to <c>trash\</c> of the profile — with its history,
    ///     so nothing is lost by a wrong click.
    /// </summary>
    /// <returns>Where the folder went.</returns>
    public async Task<string> RemoveAsync(string id)
    {
        var state = Get(id);
        await host.UnloadAsync(id).ConfigureAwait(false);
        var trash = Path.Combine(paths.ProfileRoot, "trash", $"{ExtensionBuilder.AssemblyName(id)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        try
        {
            Directory.Move(state.Descriptor.Directory, trash);
        }
        catch (IOException)
        {
            // Another volume (a network share): copy, then delete
            CopyDirectory(state.Descriptor.Directory, trash);
            Directory.Delete(state.Descriptor.Directory, true);
        }

        store.SetPinnedVersion(id, null);
        return trash;
    }

    /// <summary>Turns an extension on or off and loads it again.</summary>
    public async Task<ExtensionState?> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        Get(id);
        store.SetEnabled(id, enabled);
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The files of an extension that belong to it: what git keeps, without builds.</summary>
    public IReadOnlyList<ExtensionFile> ListFiles(string id)
    {
        var directory = Get(id).Descriptor.Directory;
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => (Full: file, Relative: Path.GetRelativePath(directory, file).Replace('\\', '/')))
            .Where(file => !IsHidden(file.Relative))
            .Select(file => new ExtensionFile(file.Relative, new FileInfo(file.Full).Length, File.GetLastWriteTimeUtc(file.Full)))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads a text file of an extension.</summary>
    public string ReadFile(string id, string path)
    {
        var full = Resolve(id, path);
        if (!File.Exists(full))
        {
            throw new CommandException(ExtensionErrorCodes.FileNotFound, $"The extension '{id}' has no file {path}.");
        }

        if (new FileInfo(full).Length > MaxReadBytes)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"{path} is larger than {MaxReadBytes / 1024 / 1024} MB.");
        }

        return File.ReadAllText(full);
    }

    /// <summary>Writes a text file of an extension; the folder watcher then reloads it. Returns the compiler's verdict for a source.</summary>
    public async Task<ExtensionState?> WriteFileAsync(string id, string path, string content, CancellationToken cancellationToken)
    {
        var full = Resolve(id, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, cancellationToken).ConfigureAwait(false);
        // Not left to the watcher: the caller wants to know at once whether the code compiles
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a file of an extension.</summary>
    public async Task<ExtensionState?> DeleteFileAsync(string id, string path, CancellationToken cancellationToken)
    {
        var full = Resolve(id, path);
        if (!File.Exists(full))
        {
            throw new CommandException(ExtensionErrorCodes.FileNotFound, $"The extension '{id}' has no file {path}.");
        }

        File.Delete(full);
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The ignore list a history of this extension starts with.</summary>
    public static IReadOnlyList<string> IgnoreFor(ExtensionDescriptor descriptor)
    {
        return descriptor.Kind == ExtensionKind.Dll ? ExtensionScaffold.DllIgnore : ExtensionScaffold.SourceIgnore;
    }

    /// <summary>The version summary of an extension: whether it has a history, what changed, the latest version, the pin.</summary>
    public ExtensionVersionInfo GetVersionInfo(string id)
    {
        var state = Get(id);
        var directory = state.Descriptor.Directory;
        var initialized = history.IsInitialized(directory);
        var latest = initialized ? history.GetHistory(directory, limit: 1).FirstOrDefault() : null;
        var pinned = store.GetPinnedVersion(id);
        return new ExtensionVersionInfo(
            history.IsAvailable,
            history.UnavailableReason,
            initialized,
            initialized ? history.GetChanges(directory) : [],
            latest,
            pinned is null ? null : history.Find(directory, pinned) ?? new ExtensionVersion(pinned, pinned[..Math.Min(7, pinned.Length)], "(not found)", "", default),
            state.LoadedVersion);
    }

    /// <summary>Saves the current files as a version.</summary>
    public ExtensionVersion? SaveVersion(string id, string? message)
    {
        var state = Get(id);
        return history.SaveVersion(state.Descriptor.Directory, Author, message ?? "", IgnoreFor(state.Descriptor));
    }

    /// <summary>Brings back a version (of the whole extension or one file) and loads the result.</summary>
    public async Task<ExtensionVersion?> RestoreAsync(string id, string versionId, string? file, CancellationToken cancellationToken)
    {
        var state = Get(id);
        var version = history.Restore(state.Descriptor.Directory, Author, versionId, file);
        await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
        return version;
    }

    /// <summary>Pins the extension to a version — it runs that version's code whatever the files say — or unpins it.</summary>
    public async Task<ExtensionState?> PinAsync(string id, string? versionId, CancellationToken cancellationToken)
    {
        var state = Get(id);
        if (versionId is not null)
        {
            if (state.Descriptor.Kind != ExtensionKind.Source)
            {
                throw new CommandException(ExtensionErrorCodes.Invalid, "Only an extension built from sources can be pinned to a version; restore a version of a DLL extension instead.");
            }

            var version = history.Find(state.Descriptor.Directory, versionId)
                          ?? throw new CommandException(CommandErrorCodes.NotFound, $"There is no version {versionId} in '{id}'.");
            versionId = version.Id;
        }

        store.SetPinnedVersion(id, versionId);
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A path inside the extension folder, never outside it nor into <c>.git</c>.</summary>
    private string Resolve(string id, string path)
    {
        var directory = Path.GetFullPath(Get(id).Descriptor.Directory);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A file path is required.");
        }

        var full = Path.GetFullPath(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(directory, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || IsHidden(relative.Replace('\\', '/')))
        {
            throw new CommandException(ExtensionErrorCodes.FileNotFound, $"{path} is not a file of the extension '{id}'.");
        }

        return full;
    }

    private static bool IsHidden(string relative)
    {
        var first = relative.Split('/')[0];
        return first is ".git" or "bin" or "obj" or ".vs" or ".editor" or "node_modules";
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
        }
    }
}

/// <summary>A file of an extension.</summary>
/// <param name="Path">Relative to the extension folder, with <c>/</c>.</param>
/// <param name="Size">In bytes.</param>
/// <param name="Modified">When it last changed (UTC).</param>
public sealed record ExtensionFile(string Path, long Size, DateTime Modified);

/// <summary>The versions of an extension at a glance.</summary>
/// <param name="Available">Git can be used.</param>
/// <param name="UnavailableReason">Why it cannot.</param>
/// <param name="HasHistory">The folder has saved versions.</param>
/// <param name="Changes">The files changed since the latest version.</param>
/// <param name="Latest">The latest version.</param>
/// <param name="Pinned">The version the extension is pinned to.</param>
/// <param name="LoadedVersion">The version whose code is loaded: the pinned one, or <see langword="null" /> for the current files.</param>
public sealed record ExtensionVersionInfo(
    bool Available,
    string? UnavailableReason,
    bool HasHistory,
    IReadOnlyList<ExtensionChange> Changes,
    ExtensionVersion? Latest,
    ExtensionVersion? Pinned,
    string? LoadedVersion);
