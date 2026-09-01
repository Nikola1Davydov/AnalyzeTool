using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Dwg
{
    /// <summary>
    /// The geometry itself, filtered, as plain numbers — for a caller that wants to look at what is in a
    /// drawing before anything is created, or to do its own conversion. Nothing enters the Revit
    /// document.
    /// </summary>
    [RevitCommand(
        Description = "Returns the geometry of a .dwg or .dxf file: lines, arcs, circles, ellipses, " +
                      "polylines (with per-vertex bulge), text and block references, filtered by layer " +
                      "and by DXF type. Coordinates are in DRAWING units and angles in radians; the " +
                      "result's units field says which unit, and code 0 means the file is UNITLESS and " +
                      "the caller must decide. Nothing is imported — the Revit document is untouched. " +
                      "Layer names come from GetDwgStructure. Read-only. Cost: one full parse of the " +
                      "file plus one JSON object per returned entity, so keep maxEntities sane.",
        ReadOnly = true,
        InputType = typeof(GetDwgEntities.Request),
        OutputType = typeof(DwgEntities))]
    internal sealed class GetDwgEntities : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            if (string.IsNullOrWhiteSpace(req.Path))
                throw new ArgumentException("path is required — give the full path of a .dwg or .dxf file.");

            return await new DwgSidecarClient()
                .ReadAsync(req.Path, req.Layers, req.Types, req.Space, req.MaxEntities, req.Failsafe, ct)
                .ConfigureAwait(false);
        }

        public sealed class Request
        {
            [Description("Full path of the .dwg or .dxf file to read.")]
            public string Path { get; set; } = string.Empty;

            [Description("Layer names to keep, matched case-insensitively. Omit or leave empty for every " +
                         "layer. Names come from GetDwgStructure.")]
            public IReadOnlyList<string>? Layers { get; set; }

            [Description("DXF type names to keep, e.g. [\"LINE\",\"LWPOLYLINE\",\"ARC\"]. Omit for every " +
                         "type. The counts in GetDwgStructure's byType say which are present.")]
            public IReadOnlyList<string>? Types { get; set; }

            [Description("Which part of the drawing to read: 'model' (default), 'paper' or 'all'.")]
            public string? Space { get; set; }

            [Description("Cap on returned entities. Defaults to 20000 and is capped at 200000. When the " +
                         "result's truncated flag is set you are looking at a prefix, and matched says " +
                         "how many there really were.")]
            public int? MaxEntities { get; set; }

            [Description("Error-tolerant parsing: collect diagnostics instead of failing on the first " +
                         "unreadable object.")]
            public bool Failsafe { get; set; }
        }
    }
}
