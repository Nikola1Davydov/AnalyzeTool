using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Get
{
    [RevitCommand("GetCategoriesInRevit",
        Description = "Returns the names of all element categories present in the active document. " +
                      "Use these names with GetDataByCategoryName.",
        ReadOnly = true)]
    internal sealed class GetCategoriesInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
            {
                DataElementsCollectorService collector = new DataElementsCollectorService();
                return collector.GetModelCategoriesNames(app.ActiveUIDocument.Document);
            });
    }
}
