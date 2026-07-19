using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using System.ComponentModel;
using RevitCanceled = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Starts Revit's interactive placement for one loadable family type: activates the FamilySymbol if
    /// needed, then hands off to <c>UIDocument.PromptForFamilyInstancePlacement</c> so the user clicks to
    /// place instances until they press Escape. Backs the "Place" action in the dockable palette. Only
    /// loadable families can be placed this way — system types (walls/floors/pipes) return ok=false.
    /// </summary>
    [RevitCommand(
        Description = "Starts interactive placement of the given loadable family type (FamilySymbol). " +
                      "Returns ok=false for a non-loadable/system type.",
        InputType = typeof(PlaceFamilyInstance.Request))]
    internal sealed class PlaceFamilyInstance : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Autodesk.Revit.UI.UIDocument uidoc = app.ActiveUIDocument;
                Document doc = uidoc.Document;

                if (doc.GetElement(new ElementId(req.TypeId)) is not FamilySymbol symbol)
                    return new { ok = false, error = "This type cannot be placed from the palette (not a loadable family)." };

                // A symbol must be active before it can be placed. Activation is a model change, so it
                // needs its own transaction (placement itself is handled by Revit, no transaction here).
                if (!symbol.IsActive)
                {
                    using Transaction t = new(doc, "Family Manager: activate type");
                    t.Start();
                    SwallowWarningsPreprocessor.Apply(t);
                    symbol.Activate();
                    doc.Regenerate();
                    t.Commit();
                }

                try
                {
                    uidoc.PromptForFamilyInstancePlacement(symbol);
                }
                catch (RevitCanceled)
                {
                    // User pressed Escape / finished placing — a normal end, not an error.
                }

                return new { ok = true, error = (string?)null };
            });
        }

        public sealed class Request
        {
            [Description("Family type (FamilySymbol) id to place interactively.")]
            public long TypeId { get; set; }
        }
    }
}
