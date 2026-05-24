using AnalyseTool.Sdk;
using Autodesk.Revit.DB;

namespace AnalyseTool.Features.Actions
{
    [RevitCommand("IsolationInRevit",
        Description = "Temporarily isolates the given elements (by id) in the active view " +
                      "(reversible temporary hide/isolate). Pass an empty list to do nothing.")]
    internal sealed class IsolationInRevit : RevitTask<IsolationInRevit.Request>
    {
        public override Task<object?> ExecuteAsync(Request data, IRevitContext ctx, CancellationToken ct)
        {
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

        public sealed record Request
        {
            /// <summary>Element ids (Revit ElementId values) to isolate in the active view.</summary>
            public List<long> ElementIds { get; set; } = new();
        }
    }
}
