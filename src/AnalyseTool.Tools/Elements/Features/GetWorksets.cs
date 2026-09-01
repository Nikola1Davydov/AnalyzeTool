using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>
    /// Read-only list of the document's user worksets (id, name, open/editable flags, owner). Returns
    /// isWorkshared=false with an empty list for a non-workshared project. Backs the Worksets view and
    /// the "edit workset" target picker.
    /// </summary>
    [RevitCommand(
        Description = "Lists the user worksets of the active document (id, name, open/editable, owner). " +
                      "Read-only and cheap — it reads the workset table, not the elements. " +
                      "isWorkshared=false for a non-workshared project. This is where workset ids for " +
                      "SetInstancesWorkset come from.",
        ReadOnly = true,
        OutputType = typeof(WorksetsResult))]
    internal sealed class GetWorksets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
                new TypeAndWorksetService().GetWorksets(app.ActiveUIDocument.Document));
    }
}
