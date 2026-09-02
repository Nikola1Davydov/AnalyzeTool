using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AnalyseTool.Tools.Actions
{
    [RevitCommand(
        Description = "Selects the given elements (by id) in the active document. An empty list clears the " +
                      "selection. Returns { ok, selected, ignoredIds, error } — 'selected' is what Revit ended up " +
                      "holding, 'ignoredIds' the requested ids that do not exist in the document. " +
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
                // selection is what happened, and those two differ the moment an id has gone stale — and
                // the stale ones are NAMED, because "selected: 2 of 3" leaves the caller guessing which.
                ICollection<ElementId> held = uiDoc.Selection.GetElementIds();
                List<long> ignored = elementsIds.Where(id => !held.Contains(id)).Select(id => id.Value).ToList();
                return new SelectionResult(true, held.Count, null, ignored.Count == 0 ? null : ignored);
            });
        }

        internal sealed record SelectionPayload
        {
            [Description("Element ids (Revit ElementId values) to select in the active document.")]
            public List<long> ElementIds { get; set; }
        }
    }
}
