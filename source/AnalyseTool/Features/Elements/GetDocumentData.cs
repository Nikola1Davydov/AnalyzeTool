using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The title and the creation id of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns the active document's title and its stable creation id: { name, id }. Read-only and cheap — it reads no " +
    "elements. For what a session actually needs up front (units, language, levels, per-category instance counts) call " +
    "GetModelOverview instead.")]
public sealed class GetDocumentData(ActiveDocument document) : ICommand<DocumentData>
{
    /// <inheritdoc />
    public Task<DocumentData> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(
            revitDocument => new DocumentData(revitDocument.Title, revitDocument.CreationGUID.ToString()),
            context.CancellationToken);
    }
}

/// <summary>The result of <see cref="GetDocumentData" />.</summary>
/// <param name="Name">The document title.</param>
/// <param name="Id">The document's <c>CreationGUID</c>; it survives «Save As», so a copy has the same one.</param>
public sealed record DocumentData(string Name, string Id);
