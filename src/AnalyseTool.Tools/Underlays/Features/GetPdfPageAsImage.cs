using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Underlays
{
    [RevitCommand(
        Description = "Shows a PDF page or raster image placed in the model AS AN IMAGE, so a model that sees " +
                      "images can read the drawing itself — title block, dimensions, room names, what is drawn. " +
                      "Payload: { id, maxPixels? } — id of the image (or its type) from GetUnderlays. Returns a PNG " +
                      "of the page as Revit rasterised it (at the DPI GetUnderlays reports), scaled down so its " +
                      "longer edge is at most maxPixels (default 1568), as an image attachment plus 'file', the " +
                      "PNG's path on the Revit machine. Metadata only for CAD: use GetCadGeometry for DWG/DXF. " +
                      "Read-only. Cost: one PNG encode of the page.",
        ReadOnly = true,
        InputType = typeof(GetPdfPageAsImage.Request),
        OutputType = typeof(PageImageResult))]
    internal sealed class GetPdfPageAsImage : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request data = ctx.Payload.As<Request>() ?? new Request();
            if (data.Id is not { } id)
                return Task.FromResult<object?>(new PageImageResult
                {
                    Error = "id is required: the id of a PDF/raster image (or its type), as listed by GetUnderlays.",
                });

            return ctx.RunInRevitAsync<object?>(app =>
                new UnderlayImageService().Render(app.ActiveUIDocument.Document, id, data.MaxPixels));
        }

        internal sealed record Request
        {
            [Description("Required: element id of the ImageInstance (or of its ImageType), from GetUnderlays.")]
            public long? Id { get; set; }

            [Description("Optional: longest edge of the returned PNG in pixels, 256-4096, default 1568. Larger " +
                         "shows more detail and costs more tokens.")]
            public int? MaxPixels { get; set; }
        }
    }
}
