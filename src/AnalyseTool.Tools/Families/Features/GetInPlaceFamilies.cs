using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Families
{
    [RevitCommand(
        Description = "Returns the in-place family instances in the document (id, name, category). " +
                      "In-place families are modelled in the project rather than loaded from a .rfa.",
        ReadOnly = true)]
    internal sealed class GetInPlaceFamilies : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new FamiliesService().GetInPlaceFamilies(app.ActiveUIDocument.Document));
    }
}
