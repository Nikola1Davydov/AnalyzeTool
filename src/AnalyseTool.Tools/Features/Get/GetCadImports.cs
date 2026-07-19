using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Features.Get
{
    [RevitCommand(
        Description = "Returns imported (non-linked) CAD instances in the document (id, name). " +
                      "Imported CAD is often a model-hygiene concern.",
        ReadOnly = true)]
    internal sealed class GetCadImports : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ImportsService().GetCadImports(app.ActiveUIDocument.Document));
    }
}
