using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Reassigns the given family instances to a target workset (sets ELEM_PARTITION_PARAM). Returns
    /// ok=false on a non-workshared project. Backs the "edit worksets" action in the Worksets view.
    /// </summary>
    [RevitCommand(
        Description = "Moves the given elements (by id) to a target workset in the active document. " +
                      "Returns ok=false on a non-workshared project.",
        InputType = typeof(SetInstancesWorkset.Request))]
    internal sealed class SetInstancesWorkset : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new FamilyActionsService().SetInstancesWorkset(
                    app.ActiveUIDocument.Document, req.ElementIds, req.TypeIds, req.WorksetId));
        }

        public sealed class Request
        {
            [Description("Element ids (family instances) to move to the target workset.")]
            public List<long>? ElementIds { get; set; }

            [Description("Type (FamilySymbol) ids whose instances should all be moved to the target workset.")]
            public List<long>? TypeIds { get; set; }

            [Description("Target workset id.")]
            public int WorksetId { get; set; }
        }
    }
}
