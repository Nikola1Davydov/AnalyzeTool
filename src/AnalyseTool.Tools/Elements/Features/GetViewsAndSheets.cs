using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the document's views and sheets (each with id/name; views flagged if placed " +
                      "on a sheet) plus the total count of hidden elements across all views. Read-only. " +
                      "Cost: visits every view and sheet, and the hidden-element count opens each view's " +
                      "hidden set — the heaviest read here.",
        ReadOnly = true,
        OutputType = typeof(ViewsAndSheetsResult))]
    internal sealed class GetViewsAndSheets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ViewsSheetsService().GetViewsAndSheets(app.ActiveUIDocument.Document));
    }
}
