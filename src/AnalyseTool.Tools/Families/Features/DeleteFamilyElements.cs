using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using System.ComponentModel;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Deletes families and/or types from the active document. Deleting a family removes all of its
    /// instances. Covers both the single "Delete" action and "Purge unused" (the caller passes the unused
    /// ids it computed from the inventory). Runs in a transaction with a warning-swallowing handler.
    /// </summary>
    [RevitCommand(
        Description = "Deletes families and/or family types (by id) from the active document. Deleting a " +
                      "family deletes its instances. Used for both Delete and Purge-unused.",
        InputType = typeof(DeleteFamilyElements.Request))]
    internal sealed class DeleteFamilyElements : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new FamilyActionsService().DeleteFamilyElements(
                    app.ActiveUIDocument.Document, req.FamilyIds, req.TypeIds));
        }

        public sealed class Request
        {
            [Description("Family ids to delete (deletes the family and all its instances).")]
            public List<long>? FamilyIds { get; set; }

            [Description("Family type (FamilySymbol) ids to delete.")]
            public List<long>? TypeIds { get; set; }
        }
    }
}
