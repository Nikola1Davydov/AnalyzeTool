using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the names of all element categories present in the active document. " +
                      $"Use these names as 'category' in {nameof(GetElements)} and {nameof(GetCategoryParameters)}. " +
                      "Read-only and cheap — it reads the document's category table, not its elements. " +
                      "These names are LOCALISED: on a German model the category is 'Wände', not 'Walls'.",
        ReadOnly = true,
        OutputType = typeof(List<string>))]
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
