using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the document's views and sheets: { views: [{ id, name, viewType, isOnSheet }], " +
                      "sheets: [{ id, number, name }] }. Views exclude templates, sheets and Revit's browser " +
                      "pseudo-views. Read-only. Cost: visits every view and sheet once — cheap.",
        ReadOnly = true,
        OutputType = typeof(ViewsAndSheetsResult))]
    internal sealed class GetViewsAndSheets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ViewsSheetsService().GetViewsAndSheets(app.ActiveUIDocument.Document));
    }
}
