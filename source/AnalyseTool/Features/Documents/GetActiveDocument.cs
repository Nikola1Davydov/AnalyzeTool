using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Documents;

/// <summary>
///     The active document for the pages: its key goes into the <c>Document</c> header of their calls. Also an MCP tool.
/// </summary>
/// <remarks>Call it without the <c>Document</c> header: it asks which document is active, it does not work with one.</remarks>
[McpTool(ReadOnly = true)]
[Command(Description = "Returns the key and title of the active document, or null when no document is open.")]
public sealed class GetActiveDocument(ActiveDocument document) : ICommand<DocumentSummary?>
{
    /// <inheritdoc />
    public async Task<DocumentSummary?> ExecuteAsync(CommandContext context)
    {
        try
        {
            return await document.InvokeAsync(
                revitDocument => new DocumentSummary(ActiveDocument.GetKey(revitDocument), revitDocument.Title),
                context.CancellationToken);
        }
        catch (CommandException exception) when (exception.Code == DocumentErrorCodes.NoDocument)
        {
            return null;
        }
    }
}

/// <summary>The result of <see cref="GetActiveDocument" />.</summary>
/// <param name="Key">The document key from <see cref="ActiveDocument.GetKey" />.</param>
/// <param name="Title">The document title.</param>
public sealed record DocumentSummary(string Key, string Title);
