using AnalyseTool.Sdk;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Features.Get
{
    [RevitCommand(
        Description = "Returns the active document's review warnings, each with its description and the " +
                      "failing/additional element ids.",
        ReadOnly = true)]
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

        private sealed record WarningInRevitModel
        {
            public string WarningDescription { get; set; } = string.Empty;
            public List<long> FailingElements { get; set; } = new();
            public List<long> AdditionalElements { get; set; } = new();
        }
    }
}
