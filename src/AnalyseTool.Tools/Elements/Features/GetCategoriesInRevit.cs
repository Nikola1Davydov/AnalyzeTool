using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the LOCALISED names of all element categories in the active document " +
                      "(on a German model 'Wände', not 'Walls'). For commands, prefer the language-" +
                      $"independent builtInCategory from {nameof(GetModelOverview)}.categories — " +
                      $"{nameof(GetElements)} and {nameof(GetCategoryParameters)} take it directly and " +
                      "it never depends on the UI language. Read-only and cheap — it reads the category " +
                      "table, not the elements.",
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
