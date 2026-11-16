namespace AnalyseTool.Extensions.Versions;

/// <summary>A saved version of an extension: a git commit.</summary>
/// <param name="Id">The commit id.</param>
/// <param name="ShortId">The first seven characters of the id.</param>
/// <param name="Message">What the user wrote when saving.</param>
/// <param name="Author">Who saved it.</param>
/// <param name="Time">When.</param>
public sealed record ExtensionVersion(string Id, string ShortId, string Message, string Author, DateTimeOffset Time);

/// <summary>A file changed since the last saved version.</summary>
/// <param name="File">The path relative to the extension folder, with <c>/</c>.</param>
/// <param name="State"><c>added</c>, <c>modified</c>, <c>deleted</c> or <c>renamed</c>.</param>
public sealed record ExtensionChange(string File, string State);

/// <summary>One file of a comparison of two states of an extension.</summary>
/// <param name="File">The path relative to the extension folder, with <c>/</c>.</param>
/// <param name="State"><c>added</c>, <c>modified</c> or <c>deleted</c>.</param>
/// <param name="Before">The text before; <see langword="null" /> when the file did not exist or is binary.</param>
/// <param name="After">The text after; <see langword="null" /> when the file is gone or is binary.</param>
/// <param name="Binary">The file is not text: only the fact that it changed is known.</param>
public sealed record ExtensionFileDiff(string File, string State, string? Before, string? After, bool Binary);

/// <summary>
///     The versions of an extension folder. Git is hidden behind it: the pages and AI clients say «save a version», «history»,
///     «restore», never commit or branch.
/// </summary>
public interface IExtensionHistory
{
    /// <summary>Gets a value indicating whether git can be used at all; <see cref="UnavailableReason" /> says why not.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets why git cannot be used; <see langword="null" /> when it can.</summary>
    string? UnavailableReason { get; }

    /// <summary>Whether the folder has a history already.</summary>
    bool IsInitialized(string folder);

    /// <summary>Starts the history of a folder and saves its files as the first version.</summary>
    /// <param name="folder">The extension folder.</param>
    /// <param name="author">Who saves.</param>
    /// <param name="message">The message of the first version.</param>
    /// <param name="ignore">The lines of the <c>.gitignore</c> written when there is none.</param>
    ExtensionVersion? Init(string folder, string author, string message, IReadOnlyList<string> ignore);

    /// <summary>The files changed since the last version.</summary>
    IReadOnlyList<ExtensionChange> GetChanges(string folder);

    /// <summary>Saves every file as a new version — the first one of a folder without history; <see langword="null" /> when nothing changed.</summary>
    ExtensionVersion? SaveVersion(string folder, string author, string message, IReadOnlyList<string> ignore);

    /// <summary>The versions, newest first; of one file only when <paramref name="file" /> is given.</summary>
    IReadOnlyList<ExtensionVersion> GetHistory(string folder, string? file = null, int limit = 100);

    /// <summary>Finds a version by its full or short id.</summary>
    ExtensionVersion? Find(string folder, string versionId);

    /// <summary>The texts of the files of a version whose names end with one of <paramref name="extensions" />.</summary>
    IReadOnlyDictionary<string, string> GetFiles(string folder, string versionId, IReadOnlyCollection<string> extensions);

    /// <summary>
    ///     Compares two states: <paramref name="from" /> and <paramref name="to" /> are version ids; a missing
    ///     <paramref name="from" /> is the version before <paramref name="to" />, a missing <paramref name="to" /> is the current
    ///     files.
    /// </summary>
    IReadOnlyList<ExtensionFileDiff> Diff(string folder, string? from, string? to);

    /// <summary>
    ///     Brings back a version: of one file, or of the whole extension. Unsaved changes are saved as a version first, and the
    ///     restored state becomes a new version: nothing is lost, the history only grows.
    /// </summary>
    /// <returns>The new version; <see langword="null" /> when it equals the current one.</returns>
    ExtensionVersion? Restore(string folder, string author, string versionId, string? file = null);
}
