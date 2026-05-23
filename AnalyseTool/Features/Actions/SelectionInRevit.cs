using AnalyseTool.Sdk;
using Autodesk.Revit.DB;

namespace AnalyseTool.Features.Actions
{
    internal sealed class SelectionInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            SelectionPayload? list = ctx.Payload.As<SelectionPayload>();
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

        private sealed record SelectionPayload
        {
            public List<long> ElementIds { get; set; }
        }
    }
}
