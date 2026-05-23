using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Get
{
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
