using AnalyseTool.Sdk;
using Autodesk.Revit.DB;

namespace AnalyseTool.Features.Actions
{
    [RevitCommand("SelectionInRevit",
        Description = "Selects the given elements (by id) in the active document.")]
    internal sealed class SelectionInRevit : RevitTask<SelectionInRevit.SelectionPayload>
    {
        public override Task<object?> ExecuteAsync(SelectionPayload list, IRevitContext ctx, CancellationToken ct)
        {
            if (list == null) return Task.FromResult<object?>(null);

            List<ElementId> elementsIds = list.ElementIds
                .Where(x => x != null)
                .Select(x => new ElementId(x))
                .ToList();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                app.ActiveUIDocument.Selection.SetElementIds(elementsIds);
                return null;
            });
        }

        public sealed record SelectionPayload
        {
            /// <summary>Element ids (Revit ElementId values) to select.</summary>
            public List<long> ElementIds { get; set; }
        }
    }
}
