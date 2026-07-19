using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Get
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
