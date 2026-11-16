using System.IO;
using System.Text;
using AnalyseTool.Hosting.Commands;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace AnalyseTool.Extensions.Versions;

/// <summary>
///     The versions of an extension with LibGit2Sharp: git in process, nothing to install on the machine.
/// </summary>
/// <remarks>
///     <para>
///         The owner check of git is off: an extension on a network folder belongs to whoever created it, and git would refuse
///         every other user («dubious ownership»).
///     </para>
///     <para>Two people saving the same shared folder at once meet at git's <c>index.lock</c>: the second gets <see cref="ExtensionErrorCodes.HistoryLocked" />.</para>
///     <para>An extension folder that is already part of a bigger repository (a checkout of the user's own) keeps that repository: git finds it upwards.</para>
/// </remarks>
public sealed partial class LibGit2ExtensionHistory(ILogger<LibGit2ExtensionHistory>? logger = null) : IExtensionHistory
{
    private const int MaxDiffBytes = 512 * 1024;
    private static readonly Lazy<string?> LoadError = new(Load);
    private static int _loadReported;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            var error = LoadError.Value;
            if (logger is not null && Interlocked.Exchange(ref _loadReported, 1) == 0)
            {
                if (error is null)
                {
                    LogGitLoaded(logger, GlobalSettings.Version.InformationalVersion);
                }
                else
                {
                    LogGitUnavailable(logger, error);
                }
            }

            return error is null;
        }
    }

    /// <inheritdoc />
    public string? UnavailableReason => LoadError.Value;

    /// <inheritdoc />
    public bool IsInitialized(string folder)
    {
        return IsAvailable && Repository.Discover(folder) is not null;
    }

    /// <inheritdoc />
    public ExtensionVersion? Init(string folder, string author, string message, IReadOnlyList<string> ignore)
    {
        EnsureAvailable();
        if (!IsInitialized(folder))
        {
            Repository.Init(folder);
        }

        WriteIgnore(folder, ignore);
        using var repository = Open(folder);
        return Commit(repository, folder, author, message);
    }

    /// <inheritdoc />
    public IReadOnlyList<ExtensionChange> GetChanges(string folder)
    {
        if (!IsInitialized(folder))
        {
            return [];
        }

        using var repository = Open(folder);
        var prefix = Prefix(repository, folder);
        return repository.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = true, PathSpec = [prefix.Length == 0 ? "*" : prefix + "*"] })
            .Where(entry => entry.State != FileStatus.Ignored && entry.State != FileStatus.Unaltered)
            .Select(entry => new ExtensionChange(entry.FilePath[prefix.Length..], StateOf(entry.State)))
            .OrderBy(change => change.File, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public ExtensionVersion? SaveVersion(string folder, string author, string message, IReadOnlyList<string> ignore)
    {
        EnsureAvailable();
        if (!IsInitialized(folder))
        {
            return Init(folder, author, message, ignore);
        }

        using var repository = Open(folder);
        return Commit(repository, folder, author, message);
    }

    /// <inheritdoc />
    public IReadOnlyList<ExtensionVersion> GetHistory(string folder, string? file = null, int limit = 100)
    {
        if (!IsInitialized(folder))
        {
            return [];
        }

        using var repository = Open(folder);
        if (repository.Head.Tip is null)
        {
            return [];
        }

        var prefix = Prefix(repository, folder);
        var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        var path = string.IsNullOrWhiteSpace(file) ? prefix.TrimEnd('/') : prefix + Normalize(file);
        var commits = path.Length == 0
            ? repository.Commits.QueryBy(filter)
            : repository.Commits.QueryBy(path, filter).Select(entry => entry.Commit);

        return commits.Take(limit).Select(ToVersion).ToList();
    }

    /// <inheritdoc />
    public ExtensionVersion? Find(string folder, string versionId)
    {
        if (!IsInitialized(folder) || string.IsNullOrWhiteSpace(versionId))
        {
            return null;
        }

        using var repository = Open(folder);
        return Lookup(repository, versionId) is { } commit ? ToVersion(commit) : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetFiles(string folder, string versionId, IReadOnlyCollection<string> extensions)
    {
        EnsureInitialized(folder);
        using var repository = Open(folder);
        var version = Lookup(repository, versionId) ?? throw NoSuchVersion(versionId);
        var prefix = Prefix(repository, folder);
        return Files(version.Tree, prefix)
            .Where(entry => extensions.Any(extension => entry.Key.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(entry => entry.Key, entry => entry.Value.GetContentText(), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<ExtensionFileDiff> Diff(string folder, string? from, string? to)
    {
        EnsureInitialized(folder);
        using var repository = Open(folder);
        var prefix = Prefix(repository, folder);

        Commit? toCommit = to is null ? null : Lookup(repository, to) ?? throw NoSuchVersion(to);
        Commit? fromCommit = from is not null
            ? Lookup(repository, from) ?? throw NoSuchVersion(from)
            : toCommit is not null ? toCommit.Parents.FirstOrDefault() : repository.Head.Tip;

        var before = fromCommit is null ? new Dictionary<string, Blob>() : Files(fromCommit.Tree, prefix);
        var after = toCommit is not null ? Files(toCommit.Tree, prefix).ToDictionary(entry => entry.Key, entry => (Func<(byte[] Bytes, bool Exists)>)(() => (Read(entry.Value), true)), StringComparer.OrdinalIgnoreCase) : WorkingFiles(repository, folder, prefix, before.Keys);

        var result = new List<ExtensionFileDiff>();
        foreach (var file in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var oldBytes = before.TryGetValue(file, out var blob) ? Read(blob) : null;
            var newBytes = after.TryGetValue(file, out var reader) && reader() is { Exists: true } current ? current.Bytes : null;
            if (oldBytes is not null && newBytes is not null && oldBytes.AsSpan().SequenceEqual(newBytes))
            {
                continue;
            }

            var state = oldBytes is null ? "added" : newBytes is null ? "deleted" : "modified";
            var binary = IsBinary(oldBytes) || IsBinary(newBytes);
            result.Add(new ExtensionFileDiff(file, state, binary ? null : Text(oldBytes), binary ? null : Text(newBytes), binary));
        }

        return result;
    }

    /// <inheritdoc />
    public ExtensionVersion? Restore(string folder, string author, string versionId, string? file = null)
    {
        EnsureInitialized(folder);
        using var repository = Open(folder);
        var target = Lookup(repository, versionId) ?? throw NoSuchVersion(versionId);
        var prefix = Prefix(repository, folder);
        var shortId = target.Sha[..7];

        // Nothing is lost: what was not saved becomes a version of its own first
        Commit(repository, folder, author, $"Before restoring {shortId}");

        if (string.IsNullOrWhiteSpace(file))
        {
            var wanted = Files(target.Tree, prefix);
            // Files added after the version go; the version's files come back as they were
            foreach (var entry in repository.Index.Where(entry => entry.Path.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                var relative = entry.Path[prefix.Length..];
                if (!wanted.ContainsKey(relative))
                {
                    var path = Path.Combine(folder, relative);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }

            foreach (var (relative, blob) in wanted)
            {
                var path = Path.Combine(folder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, Read(blob));
            }
        }
        else
        {
            var relative = Normalize(file);
            if (target.Tree[prefix + relative] is not { TargetType: TreeEntryTargetType.Blob } entry)
            {
                throw new CommandException(ExtensionErrorCodes.FileNotFound, $"The version {shortId} has no file {relative}.");
            }

            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Read((Blob)entry.Target));
        }

        var message = string.IsNullOrWhiteSpace(file)
            ? $"Restored version {shortId}: {target.MessageShort}"
            : $"Restored {Normalize(file)} from version {shortId}";
        return Commit(repository, folder, author, message);
    }

    private ExtensionVersion? Commit(Repository repository, string folder, string author, string message)
    {
        try
        {
            var prefix = Prefix(repository, folder);
            Commands.Stage(repository, prefix.Length == 0 ? "*" : prefix + "*");
            var status = repository.RetrieveStatus(new StatusOptions { IncludeUntracked = false });
            var nothingStaged = repository.Head.Tip is not null && !status.Any(entry =>
                entry.State.HasFlag(FileStatus.NewInIndex) || entry.State.HasFlag(FileStatus.ModifiedInIndex) ||
                entry.State.HasFlag(FileStatus.DeletedFromIndex) || entry.State.HasFlag(FileStatus.RenamedInIndex) ||
                entry.State.HasFlag(FileStatus.TypeChangeInIndex));
            if (nothingStaged)
            {
                return null;
            }

            var signature = new Signature(author, $"{author}@{Environment.MachineName}", DateTimeOffset.Now);
            var text = string.IsNullOrWhiteSpace(message) ? "Version" : message.Trim();
            var version = ToVersion(repository.Commit(text, signature, signature, new CommitOptions { AllowEmptyCommit = repository.Head.Tip is null }));
            if (logger is not null)
            {
                LogSaved(logger, version.ShortId, folder, author);
            }

            return version;
        }
        catch (LockedFileException exception)
        {
            throw new CommandException(ExtensionErrorCodes.HistoryLocked, $"Another save of this extension is running: try again in a moment. ({exception.Message})");
        }
    }

    private static Repository Open(string folder)
    {
        return new Repository(Repository.Discover(folder));
    }

    /// <summary>The path of the extension folder inside its repository, with a trailing <c>/</c>; empty when the folder is the repository.</summary>
    private static string Prefix(Repository repository, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.Info.WorkingDirectory));
        var relative = Path.GetRelativePath(root, Path.GetFullPath(folder)).Replace('\\', '/');
        return relative is "." or "" ? "" : relative.TrimEnd('/') + "/";
    }

    private static Dictionary<string, Blob> Files(Tree tree, string prefix)
    {
        var files = new Dictionary<string, Blob>(StringComparer.OrdinalIgnoreCase);
        var start = prefix.Length == 0 ? tree : tree[prefix.TrimEnd('/')]?.Target as Tree;
        if (start is not null)
        {
            Collect(start, "");
        }

        return files;

        void Collect(Tree current, string path)
        {
            foreach (var entry in current)
            {
                if (entry.TargetType == TreeEntryTargetType.Tree)
                {
                    Collect((Tree)entry.Target, path + entry.Name + "/");
                }
                else if (entry.Target is Blob blob)
                {
                    files[path + entry.Name] = blob;
                }
            }
        }
    }

    /// <summary>The files of the working folder that git does not ignore, plus those of <paramref name="known" /> (to see deletions).</summary>
    private static Dictionary<string, Func<(byte[] Bytes, bool Exists)>> WorkingFiles(Repository repository, string folder, string prefix, IEnumerable<string> known)
    {
        var files = new Dictionary<string, Func<(byte[] Bytes, bool Exists)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (relative.StartsWith(".git/", StringComparison.Ordinal) || repository.Ignore.IsPathIgnored(prefix + relative))
            {
                continue;
            }

            files[relative] = () => (File.ReadAllBytes(path), true);
        }

        foreach (var relative in known)
        {
            files.TryAdd(relative, () => ([], false));
        }

        return files;
    }

    private static byte[] Read(Blob blob)
    {
        using var stream = blob.GetContentStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool IsBinary(byte[]? bytes)
    {
        return bytes is not null && (bytes.Length > MaxDiffBytes || bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0));
    }

    private static string? Text(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static Commit? Lookup(Repository repository, string versionId)
    {
        try
        {
            return repository.Lookup<Commit>(versionId.Trim());
        }
        catch (AmbiguousSpecificationException)
        {
            return null;
        }
    }

    private static string Normalize(string file)
    {
        return file.Replace('\\', '/').TrimStart('/');
    }

    private static ExtensionVersion ToVersion(Commit commit)
    {
        return new ExtensionVersion(commit.Sha, commit.Sha[..7], commit.Message.TrimEnd(), commit.Author.Name, commit.Author.When);
    }

    private static string StateOf(FileStatus state)
    {
        if (state.HasFlag(FileStatus.NewInWorkdir) || state.HasFlag(FileStatus.NewInIndex))
        {
            return "added";
        }

        if (state.HasFlag(FileStatus.DeletedFromWorkdir) || state.HasFlag(FileStatus.DeletedFromIndex))
        {
            return "deleted";
        }

        return state.HasFlag(FileStatus.RenamedInWorkdir) || state.HasFlag(FileStatus.RenamedInIndex) ? "renamed" : "modified";
    }

    private static void WriteIgnore(string folder, IReadOnlyList<string> ignore)
    {
        var path = Path.Combine(folder, ".gitignore");
        if (!File.Exists(path) && ignore.Count > 0)
        {
            File.WriteAllText(path, string.Join("\n", ignore) + "\n");
        }
    }

    private static CommandException NoSuchVersion(string versionId)
    {
        return new CommandException(CommandErrorCodes.NotFound, $"There is no version {versionId} in this extension.");
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new CommandException(ExtensionErrorCodes.HistoryUnavailable, $"Versions are not available: {UnavailableReason}");
        }
    }

    private void EnsureInitialized(string folder)
    {
        EnsureAvailable();
        if (!IsInitialized(folder))
        {
            throw new CommandException(ExtensionErrorCodes.HistoryUnavailable, "The extension has no history yet: save its first version.");
        }
    }

    /// <summary>Loads the native libgit2 once; the error text when it cannot load.</summary>
    private static string? Load()
    {
        try
        {
            GlobalSettings.SetOwnerValidation(false);
            _ = GlobalSettings.Version;
            return null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException)
        {
            return $"the git library could not be loaded ({exception.GetBaseException().Message})";
        }
    }

    [LoggerMessage(LogLevel.Information, "Git for the versions of extensions is loaded: libgit2 {Version}")]
    private static partial void LogGitLoaded(ILogger logger, string version);

    [LoggerMessage(LogLevel.Warning, "Versions of extensions are off: {Reason}")]
    private static partial void LogGitUnavailable(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Information, "Saved version {Version} of {Folder} by {Author}")]
    private static partial void LogSaved(ILogger logger, string version, string folder, string author);
}
