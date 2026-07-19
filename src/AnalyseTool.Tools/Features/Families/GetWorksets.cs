using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Features.Families
{
    /// <summary>
    /// Read-only list of the document's user worksets (id, name, open/editable flags, owner). Returns
    /// isWorkshared=false with an empty list for a non-workshared project. Backs the Worksets view and
    /// the "edit workset" target picker.
    /// </summary>
    [RevitCommand(
        Description = "Lists the user worksets of the active document (id, name, open/editable, owner). " +
                      "Read-only. isWorkshared=false for a non-workshared project.",
        ReadOnly = true)]
    internal sealed class GetWorksets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
                new FamiliesService().GetWorksets(app.ActiveUIDocument.Document));
    }
}
