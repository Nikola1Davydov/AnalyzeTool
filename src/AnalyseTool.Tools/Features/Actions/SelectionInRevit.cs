using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AnalyseTool.Tools.Features.Actions
{
    [RevitCommand(
        Description = "Selects the given elements (by id) in the active document.",
        InputType = typeof(SelectionInRevit.SelectionPayload))]
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
                app.ActiveUIDocument.Selection.SetElementIds(elementsIds);
                return null;
            });
        }

        internal sealed record SelectionPayload
        {
            [Description("Element ids (Revit ElementId values) to select in the active document.")]
            public List<long> ElementIds { get; set; }
        }
    }
}
