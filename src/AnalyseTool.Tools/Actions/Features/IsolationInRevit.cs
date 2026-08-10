using AnalyseTool.Sdk;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AnalyseTool.Tools.Actions
{
    [RevitCommand(
        Description = "Temporarily isolates the given elements (by id) in the active view " +
                      "(reversible temporary hide/isolate). Pass an empty list to do nothing. " +
                      "Changes the ACTIVE VIEW, not the model, and is undone by resetting temporary " +
                      "hide/isolate. Cost: one view transaction — cheap.",
        InputType = typeof(IsolationInRevit.Request),
        OutputType = typeof(IsolationResult))]
    internal sealed class IsolationInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            List<ElementId> elementsIds = (data?.ElementIds ?? new List<long>())
                .Select(x => new ElementId(x))
                .ToList();
            if (elementsIds.Count == 0)
                return Task.FromResult<object?>(new IsolationResult(true, 0, null, Array.Empty<TransactionWarning>()));

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                View view = doc.ActiveView;
                // Reported rather than returned as a bare null: a caller with nobody watching the screen
                // cannot tell "nothing to do" apart from "the active view refused the change".
                if (!view.IsModifiable)
                    return new IsolationResult(false, 0, "The active view cannot be modified.", Array.Empty<TransactionWarning>());

                using Transaction transaction = new Transaction(doc, "Isolate");
                transaction.Start();
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);

                if (view.IsTemporaryHideIsolateActive())
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                view.IsolateElementsTemporary(elementsIds);
                transaction.Commit();
                return new IsolationResult(true, elementsIds.Count, null, failures.Warnings);
            });
        }

        internal sealed record Request
        {
            [Description("Element ids (Revit ElementId values) to isolate in the active view.")]
            public List<long> ElementIds { get; set; } = new();
        }
    }
}
