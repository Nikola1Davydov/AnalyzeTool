using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns imported (non-linked) CAD instances in the document (id, name) — a model-hygiene " +
                      "check. To understand what a DWG/PDF underlay contains (layers, geometry, page, scale), use " +
                      "GetUnderlays instead. Read-only. Cost: one scan of the document's import instances.",
        ReadOnly = true,
        OutputType = typeof(List<ImportInfo>))]
    internal sealed class GetCadImports : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ImportsService().GetCadImports(app.ActiveUIDocument.Document));
    }
}
