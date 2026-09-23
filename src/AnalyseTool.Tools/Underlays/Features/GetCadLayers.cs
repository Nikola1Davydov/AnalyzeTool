using AnalyseTool.Sdk;
using AnalyseTool.Sdk.Underlays;
using System.ComponentModel;

namespace AnalyseTool.Tools.Underlays
{
    [RevitCommand(
        Description = "Returns the layers of one CAD import/link (DWG, DXF…): name, colour, line weight, line " +
                      "pattern, whether the layer is hidden in a view, and how many primitives of each type " +
                      "(line, polyline, arc, circle, block…) it holds. Use it to decide which layer is which " +
                      "before GetCadGeometry. Payload: { importId, viewId? } — importId from GetUnderlays; " +
                      "visibility refers to viewId, else to the owner view of a view-specific import. " +
                      "Read-only. Cost: walks the file's geometry once.",
        ReadOnly = true,
        InputType = typeof(GetCadLayers.Request),
        OutputType = typeof(CadLayersResult))]
    internal sealed class GetCadLayers : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request data = ctx.Payload.As<Request>() ?? new Request();
            if (data.ImportId is not { } importId)
                return Task.FromResult<object?>(new CadLayersResult { Error = MissingImportId });

            return ctx.RunInRevitAsync<object?>(app =>
                UnderlayReader.GetCadLayers(app.ActiveUIDocument.Document, importId, data.ViewId));
        }

        internal const string MissingImportId =
            "importId is required: the id of a CAD import or link, as listed by GetUnderlays.";

        internal sealed record Request
        {
            [Description("Required: element id of the CAD import or link (ImportInstance), from GetUnderlays.")]
            public long? ImportId { get; set; }

            [Description("Optional: the view to report layer visibility ('hiddenInView') for.")]
            public long? ViewId { get; set; }
        }
    }
}
