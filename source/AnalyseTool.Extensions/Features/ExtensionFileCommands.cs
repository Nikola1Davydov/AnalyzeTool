using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Extensions.Features;

/// <summary>Lists the files of an extension.</summary>
[McpTool(ReadOnly = true, Title = "List extension files")]
[Command(Description = "Lists the files of an extension (paths relative to its folder), without builds and git internals.")]
public sealed class ListExtensionFiles(ExtensionManager manager) : ICommand<ExtensionRequest, IReadOnlyList<ExtensionFile>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ExtensionFile>> ExecuteAsync(ExtensionRequest request, CommandContext context)
    {
        return Task.FromResult(manager.ListFiles(request.Id));
    }
}

/// <summary>Input of <see cref="ReadExtensionFile" />.</summary>
public sealed record ExtensionFileRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("The file, relative to the extension folder, e.g. src/CountWalls.cs or plugin.json.")] string Path);

/// <summary>A text file of an extension.</summary>
/// <param name="Path">Relative to the extension folder.</param>
/// <param name="Content">The text.</param>
public sealed record ExtensionFileContent(string Path, string Content);

/// <summary>Reads a text file of an extension.</summary>
[McpTool(ReadOnly = true, Title = "Read extension file")]
[Command(Description = "Reads a text file of a button or an extension, e.g. Renumber.cs or bundle.json (a plugin.json folder: src/CountWalls.cs, plugin.json).")]
public sealed class ReadExtensionFile(ExtensionManager manager) : ICommand<ExtensionFileRequest, ExtensionFileContent>
{
    /// <inheritdoc />
    public Task<ExtensionFileContent> ExecuteAsync(ExtensionFileRequest request, CommandContext context)
    {
        return Task.FromResult(new ExtensionFileContent(request.Path, manager.ReadFile(request.Id, request.Path)));
    }
}

/// <summary>Input of <see cref="WriteExtensionFile" />.</summary>
public sealed record WriteExtensionFileRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("The file, relative to the extension folder, e.g. src/CountWalls.cs. Folders are created.")] string Path,
    [property: Description("The whole new text of the file.")] string Content);

/// <summary>Writes a text file of an extension and loads the result.</summary>
[McpTool(Title = "Write extension file")]
[Command(Description = "Writes (creates or replaces) a text file of an extension and reloads it at once: a C# source is compiled, and the result carries the compiler errors. The change is not a saved version until SaveExtensionVersion.")]
public sealed class WriteExtensionFile(ExtensionManager manager) : ICommand<WriteExtensionFileRequest, ExtensionSummary?>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary?> ExecuteAsync(WriteExtensionFileRequest request, CommandContext context)
    {
        var state = await manager.WriteFileAsync(request.Id, request.Path, request.Content, context.CancellationToken).ConfigureAwait(false);
        return state is null ? null : ExtensionSummary.From(state);
    }
}

/// <summary>Deletes a file of an extension.</summary>
[McpTool(Destructive = true, Title = "Delete extension file")]
[Command(Description = "Deletes a file of an extension and reloads it. A saved version still holds the file: RestoreExtensionVersion brings it back.")]
public sealed class DeleteExtensionFile(ExtensionManager manager) : ICommand<ExtensionFileRequest, ExtensionSummary?>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary?> ExecuteAsync(ExtensionFileRequest request, CommandContext context)
    {
        var state = await manager.DeleteFileAsync(request.Id, request.Path, context.CancellationToken).ConfigureAwait(false);
        return state is null ? null : ExtensionSummary.From(state);
    }
}
