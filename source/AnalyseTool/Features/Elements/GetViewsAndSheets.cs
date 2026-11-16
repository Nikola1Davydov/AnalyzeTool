using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The views and sheets of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns the document's views and sheets: { views: [{ id, name, viewType, isOnSheet }], sheets: [{ id, number, name }] }. " +
    "Views exclude templates, sheets and Revit's browser pseudo-views. Read-only. Cost: visits every view and sheet once — cheap.")]
public sealed class GetViewsAndSheets(ActiveDocument document, ViewsSheetsService service) : ICommand<ViewsAndSheetsResult>
{
    /// <inheritdoc />
    public Task<ViewsAndSheetsResult> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.GetViewsAndSheets, context.CancellationToken);
    }
}
