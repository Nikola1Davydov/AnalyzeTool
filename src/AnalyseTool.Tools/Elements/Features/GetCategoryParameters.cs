using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the parameters available on elements of a Revit category, sampled from a " +
                      "representative element: { category, builtInCategory, count, parameters: [{ id, " +
                      "builtInParameter, name, storageType, isReadOnly, isType }], error, didYouMean }. " +
                      "'id' is what SetDataToParameters takes. Identify the category by the language-" +
                      "independent builtInCategory (e.g. \"OST_Walls\", from GetModelOverview.categories) — " +
                      "the localised name works too but is a guess on a German model. Read-only. Cost: " +
                      "samples one element instead of scanning the category.",
        ReadOnly = true,
        InputType = typeof(GetCategoryParameters.Request),
        OutputType = typeof(CategoryParametersResult))]
    internal sealed class GetCategoryParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            ElementQuery query = new() { Category = data?.Category, BuiltInCategory = data?.BuiltInCategory };

            return ctx.RunInRevitAsync<object?>(app =>
                new DataElementsCollectorService().GetCategoryParameters(app.ActiveUIDocument.Document, query));
        }

        internal sealed record Request
        {
            [Description("Language-independent BuiltInCategory name, e.g. \"OST_Walls\" — preferred. GetModelOverview.categories lists the ones with elements.")]
            public string? BuiltInCategory { get; set; }

            [Description("Localised category name as shown in Revit (e.g. \"Wände\" on a German model), when builtInCategory is not given.")]
            public string? Category { get; set; }
        }
    }
}
