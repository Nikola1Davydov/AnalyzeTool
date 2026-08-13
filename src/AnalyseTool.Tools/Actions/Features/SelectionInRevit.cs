using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AnalyseTool.Tools.Actions
{
    [RevitCommand(
        Description = "Selects the given elements (by id) in the active document. An empty list clears the " +
                      "selection. Returns { ok, selected, error } — 'selected' is what Revit ended up holding. " +
                      "Changes the UI selection, not the model. Cheap.",
        InputType = typeof(SelectionInRevit.SelectionPayload),
        OutputType = typeof(SelectionResult))]
    internal sealed class SelectionInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            SelectionPayload? list = ctx.Payload.As<SelectionPayload>();

            List<ElementId> elementsIds = (list?.ElementIds ?? new List<long>())
                .Select(x => new ElementId(x))
                .ToList();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                // Reported rather than thrown: with no document open there is nothing to select in, and a
                // caller with nobody watching the screen deserves that sentence over a NullReference.
                Autodesk.Revit.UI.UIDocument? uiDoc = app.ActiveUIDocument;
                if (uiDoc is null)
                    return new SelectionResult(false, 0, "No active document.");

                uiDoc.Selection.SetElementIds(elementsIds);

                // Read back instead of returning elementsIds.Count: the request is what was asked for, the
                // selection is what happened, and those two differ the moment an id has gone stale.
                return new SelectionResult(true, uiDoc.Selection.GetElementIds().Count, null);
            });
        }

        internal sealed record SelectionPayload
        {
            [Description("Element ids (Revit ElementId values) to select in the active document.")]
            public List<long> ElementIds { get; set; }
        }
    }
}
