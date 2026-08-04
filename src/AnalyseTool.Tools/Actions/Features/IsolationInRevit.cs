using AnalyseTool.Sdk;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AnalyseTool.Tools.Actions
{
    [RevitCommand(
        Description = "Temporarily isolates the given elements (by id) in the active view " +
                      "(reversible temporary hide/isolate). Pass an empty list to do nothing.",
        InputType = typeof(IsolationInRevit.Request))]
    internal sealed class IsolationInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            List<ElementId> elementsIds = (data?.ElementIds ?? new List<long>())
                .Select(x => new ElementId(x))
                .ToList();
            if (elementsIds.Count == 0)
                return Task.FromResult<object?>(new { ok = true, isolated = 0 });

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                View view = doc.ActiveView;
                // Reported rather than returned as a bare null: a caller with nobody watching the screen
                // cannot tell "nothing to do" apart from "the active view refused the change".
                if (!view.IsModifiable)
                    return new { ok = false, isolated = 0, error = "The active view cannot be modified." };

                using Transaction transaction = new Transaction(doc, "Isolate");
                transaction.Start();
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);

                if (view.IsTemporaryHideIsolateActive())
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                view.IsolateElementsTemporary(elementsIds);
                transaction.Commit();
                return new { ok = true, isolated = elementsIds.Count, warnings = failures.Warnings };
            });
        }

        internal sealed record Request
        {
            [Description("Element ids (Revit ElementId values) to isolate in the active view.")]
            public List<long> ElementIds { get; set; } = new();
        }
    }
}
