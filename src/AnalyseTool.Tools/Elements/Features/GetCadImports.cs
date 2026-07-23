using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
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
