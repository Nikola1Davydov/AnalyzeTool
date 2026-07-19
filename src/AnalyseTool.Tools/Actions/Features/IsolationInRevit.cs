using AnalyseTool.Sdk;
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
            if (elementsIds.Count == 0) return Task.FromResult<object?>(null);

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                View view = doc.ActiveView;
                if (!view.IsModifiable) return null;

                using Transaction transaction = new Transaction(doc, "Isolate");
                transaction.Start();

                if (view.IsTemporaryHideIsolateActive())
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                view.IsolateElementsTemporary(elementsIds);
                transaction.Commit();
                return null;
            });
        }

        internal sealed record Request
        {
            [Description("Element ids (Revit ElementId values) to isolate in the active view.")]
            public List<long> ElementIds { get; set; } = new();
        }
    }
}
