using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Underlays
{
    [RevitCommand(
        Description = "START HERE whenever the user mentions a DWG, DXF, CAD file, PDF, scan, image, underlay or " +
                      "\"the drawing under the plan\": one call returns every CAD import/link and PDF/raster image in " +
                      "the model — including those INSIDE loaded Revit links, e.g. a consultant's model with its own " +
                      "DWG (marked with linkInstanceId, coordinates already in this project) — or on one view, with what it IS — kind, file path and load status, linked or " +
                      "imported, the view/sheet or level it sits on, pinned, position, rotation and extent in mm — " +
                      "and what it CONTAINS: for CAD every layer with colour and primitive counts by type (so the " +
                      "grid layer, the wall layer and the door-block layer are visible at once), for a PDF the page, " +
                      "DPI, paper size and the scale it is placed at. Each underlay also carries a one-sentence " +
                      "'summary'. Filter with viewId (\"what is on this view?\") and kinds ([\"dwg\"], [\"pdf\"], " +
                      "[\"cad\"] for every CAD kind). Next steps: GetCadLayers for layer details, GetCadGeometry for " +
                      "the lines/polylines/blocks of a layer in project coordinates, GetPdfPageAsImage to look at a " +
                      "PDF. Read-only. Cost: walks each CAD file's geometry once for the layer counts — pass " +
                      "includeLayers false for a quick inventory of a model with huge DWGs.",
        ReadOnly = true,
        InputType = typeof(GetUnderlays.Request),
        OutputType = typeof(UnderlaysResult))]
    internal sealed class GetUnderlays : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request data = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                UnderlayReader.GetUnderlays(
                    app.ActiveUIDocument.Document,
                    data.ViewId,
                    data.Kinds,
                    data.IncludeLayers ?? true,
                    data.IncludeLinks ?? true));
        }

        internal sealed record Request
        {
            [Description("Optional: only underlays visible in this view or sheet — model-wide CAD imports it shows " +
                         "plus everything placed on it. Ids come from GetViewsAndSheets. Omit for the whole model.")]
            public long? ViewId { get; set; }

            [Description("Optional: only these kinds, e.g. [\"dwg\"], [\"pdf\"], [\"pdf\", \"image\"]. \"cad\" " +
                         "stands for every CAD kind (dwg, dxf, dgn, sat, skp…). Omit for all.")]
            public List<string>? Kinds { get; set; }

            [Description("Optional, default true: include the per-layer summary of each CAD file. False answers " +
                         "faster on very large files, without 'layers'.")]
            public bool? IncludeLayers { get; set; }

            [Description("Optional, default true: also report the DWG/PDF inside loaded Revit links. False answers " +
                         "for this model's own underlays only.")]
            public bool? IncludeLinks { get; set; }
        }
    }
}
