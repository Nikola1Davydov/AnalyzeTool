using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the document's views and sheets (each with id/name; views flagged if placed " +
                      "on a sheet) plus the total count of hidden elements across all views.",
        ReadOnly = true)]
    internal sealed class GetViewsAndSheets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ViewsSheetsService().GetViewsAndSheets(app.ActiveUIDocument.Document));
    }
}
