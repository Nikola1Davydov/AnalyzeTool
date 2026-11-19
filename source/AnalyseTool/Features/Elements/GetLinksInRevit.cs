using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The Revit links and linked CAD files of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns linked content in the document: { revitLinks: [{ id, name }], cadLinks: [{ id, name }] } — Revit links and " +
    "linked CAD files. Read-only. Cost: one scan of the document's link and import instances.")]
public sealed class GetLinksInRevit(ActiveDocument document, LinksService service) : ICommand<LinksResult>
{
    /// <inheritdoc />
    public Task<LinksResult> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.GetLinks, context.CancellationToken);
    }
}
