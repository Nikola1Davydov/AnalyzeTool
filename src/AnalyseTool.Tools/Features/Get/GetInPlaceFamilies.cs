using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Features.Get
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
