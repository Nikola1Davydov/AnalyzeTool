using System.ComponentModel;
using AnalyseTool.Extensions.Versions;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Extensions.Features;

/// <summary>The versions of an extension: the summary and the history.</summary>
/// <param name="Info">Whether there is a history, what changed, the latest and the pinned version.</param>
/// <param name="History">The saved versions, newest first.</param>
public sealed record ExtensionVersions(ExtensionVersionInfo Info, IReadOnlyList<ExtensionVersion> History);

/// <summary>Input of <see cref="GetExtensionVersions" />.</summary>
public sealed record ExtensionVersionsRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("Only the versions that changed this file, relative to the folder.")] string? File = null,
    [property: Description("At most this many versions; 100 by default.")] int Limit = 100);

/// <summary>Returns the versions of an extension.</summary>
[McpTool(ReadOnly = true, Title = "Extension versions")]
[Command(Description = "Returns the saved versions of an extension (newest first: id, message, author, time), the files changed since the latest one, and the version the extension is pinned to, if any.")]
public sealed class GetExtensionVersions(ExtensionManager manager) : ICommand<ExtensionVersionsRequest, ExtensionVersions>
{
    /// <inheritdoc />
    public Task<ExtensionVersions> ExecuteAsync(ExtensionVersionsRequest request, CommandContext context)
    {
        var state = manager.Get(request.Id);
        var history = manager.History.GetHistory(state.Descriptor.Directory, request.File, Math.Clamp(request.Limit, 1, 1000));
        return Task.FromResult(new ExtensionVersions(manager.GetVersionInfo(request.Id), history));
    }
}

/// <summary>Input of <see cref="SaveExtensionVersion" />.</summary>
public sealed record SaveExtensionVersionRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("What changed, in a sentence.")] string? Message = null);

/// <summary>The result of <see cref="SaveExtensionVersion" />.</summary>
/// <param name="Version">The new version; <see langword="null" /> when nothing changed.</param>
public sealed record SaveExtensionVersionResult(ExtensionVersion? Version);

/// <summary>Saves the current files of an extension as a version.</summary>
[McpTool(Title = "Save extension version")]
[Command(Description = "Saves the current files of an extension as a new version with a message (a git commit in the extension folder). Returns null when nothing changed since the latest version.")]
public sealed class SaveExtensionVersion(ExtensionManager manager) : ICommand<SaveExtensionVersionRequest, SaveExtensionVersionResult>
{
    /// <inheritdoc />
    public Task<SaveExtensionVersionResult> ExecuteAsync(SaveExtensionVersionRequest request, CommandContext context)
    {
        return Task.FromResult(new SaveExtensionVersionResult(manager.SaveVersion(request.Id, request.Message)));
    }
}

/// <summary>Input of <see cref="GetExtensionDiff" />.</summary>
public sealed record ExtensionDiffRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("The older version; the version before 'to' when absent.")] string? From = null,
    [property: Description("The newer version; the current files when absent.")] string? To = null);

/// <summary>Compares two states of an extension.</summary>
[McpTool(ReadOnly = true, Title = "Compare extension versions")]
[Command(Description = "Compares two states of an extension file by file, with the text before and after. Without 'from' and 'to' it shows the unsaved changes; with 'to' only, what that version changed.")]
public sealed class GetExtensionDiff(ExtensionManager manager) : ICommand<ExtensionDiffRequest, IReadOnlyList<ExtensionFileDiff>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ExtensionFileDiff>> ExecuteAsync(ExtensionDiffRequest request, CommandContext context)
    {
        var state = manager.Get(request.Id);
        return Task.FromResult(manager.History.Diff(state.Descriptor.Directory, request.From, request.To));
    }
}

/// <summary>Input of <see cref="RestoreExtensionVersion" />.</summary>
public sealed record RestoreExtensionVersionRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("The version to bring back (full or short id).")] string Version,
    [property: Description("Only this file, relative to the folder; the whole extension when absent.")] string? File = null);

/// <summary>Brings back a version of an extension.</summary>
[McpTool(Destructive = true, Title = "Restore extension version")]
[Command(Description = "Brings back a version of an extension, or one file of it. Unsaved changes are saved as a version first and the restored state becomes a new version, so nothing is lost. The extension is reloaded.")]
public sealed class RestoreExtensionVersion(ExtensionManager manager) : ICommand<RestoreExtensionVersionRequest, SaveExtensionVersionResult>
{
    /// <inheritdoc />
    public async Task<SaveExtensionVersionResult> ExecuteAsync(RestoreExtensionVersionRequest request, CommandContext context)
    {
        return new SaveExtensionVersionResult(await manager.RestoreAsync(request.Id, request.Version, request.File, context.CancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Input of <see cref="PinExtensionVersion" />.</summary>
public sealed record PinExtensionVersionRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("The version to run (full or short id); null to run the current files again.")] string? Version);

/// <summary>Pins an extension to a version.</summary>
[McpTool(Title = "Pin extension version")]
[Command(Description = "Pins an extension built from sources to a saved version: its button runs that version's code whatever the files say, until it is unpinned (version null). The files are not touched.")]
public sealed class PinExtensionVersion(ExtensionManager manager) : ICommand<PinExtensionVersionRequest, ExtensionSummary?>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary?> ExecuteAsync(PinExtensionVersionRequest request, CommandContext context)
    {
        var state = await manager.PinAsync(request.Id, string.IsNullOrWhiteSpace(request.Version) ? null : request.Version, context.CancellationToken).ConfigureAwait(false);
        return state is null ? null : ExtensionSummary.From(state);
    }
}
