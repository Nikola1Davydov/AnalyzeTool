using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the active document's review warnings, each with its description and the " +
                      "failing/additional element ids.",
        ReadOnly = true,
        OutputType = typeof(List<GetWarningsInRevit.WarningInRevitModel>))]
    internal sealed class GetWarningsInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                List<WarningInRevitModel> result = new();

                foreach (FailureMessage item in doc.GetWarnings())
                {
                    result.Add(new WarningInRevitModel
                    {
                        AdditionalElements = item.GetAdditionalElements().Select(x => x.Value).ToList(),
                        FailingElements = item.GetFailingElements().Select(x => x.Value).ToList(),
                        WarningDescription = item.GetDescriptionText(),
                    });
                }

                return result;
            });

        /// <summary>Internal, not private: <c>typeof(...)</c> in the attribute above has to reach it, and
        /// the SDK asks declared schema types to be at least internal for exactly that reason.</summary>
        internal sealed record WarningInRevitModel
        {
            [JsonProperty("warningDescription")]
            public string WarningDescription { get; set; } = string.Empty;

            [JsonProperty("failingElements")]
            public List<long> FailingElements { get; set; } = new();

            [JsonProperty("additionalElements")]
            public List<long> AdditionalElements { get; set; } = new();
        }
    }
}
