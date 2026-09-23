using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Underlays
{
    [RevitCommand(
        Description = "Returns the geometry of one CAD import/link (DWG, DXF…) as primitives in PROJECT coordinates, " +
                      "in mm, with the import's position, rotation, scale and nested blocks already applied — ready " +
                      "to build from (\"take the axes on layer A-GRID and create grids\": pass layers [\"A-GRID\"], " +
                      "types [\"line\"]). For a DWG inside a Revit link pass its linkInstanceId too; coordinates " +
                      "come back in THIS project. Each primitive: { type, layer, block?, points [[x,y,z]…], center?, radius?, " +
                      "length?, closed? }. Types: line, arc, circle, ellipse, spline, polyline, point, solid, mesh, " +
                      "block (a block reference: its insertion point). CAD text, dimensions and hatch patterns are not " +
                      "exposed by the Revit API. 'count' is the matches before 'limit', so a truncated answer says so; " +
                      "an unknown layer name comes back in 'unknownLayers' with 'availableLayers'. Divide mm by 304.8 " +
                      "for Revit internal feet when writing Revit API code. Read-only. Cost: walks the file's " +
                      "geometry once; the answer grows with the primitives returned — filter by layers and types.",
        ReadOnly = true,
        InputType = typeof(GetCadGeometry.Request),
        OutputType = typeof(CadGeometryResult))]
    internal sealed class GetCadGeometry : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request data = ctx.Payload.As<Request>() ?? new Request();
            if (data.ImportId is not { } importId)
                return Task.FromResult<object?>(new CadGeometryResult { Error = GetCadLayers.MissingImportId });

            return ctx.RunInRevitAsync<object?>(app =>
                UnderlayReader.GetCadGeometry(app.ActiveUIDocument.Document, importId, data.Layers, data.Types, data.Limit, data.LinkInstanceId));
        }

        internal sealed record Request
        {
            [Description("Required: element id of the CAD import or link (ImportInstance), from GetUnderlays.")]
            public long? ImportId { get; set; }

            [Description("Optional: only primitives on these layers (case-insensitive), e.g. [\"A-GRID\"]. Layer " +
                         "names come from GetUnderlays or GetCadLayers. Omit for every layer.")]
            public List<string>? Layers { get; set; }

            [Description("Optional: only these primitive types, e.g. [\"line\", \"polyline\"]. One of line, arc, " +
                         "circle, ellipse, spline, polyline, point, solid, mesh, block. Omit for all.")]
            public List<string>? Types { get; set; }

            [Description("Optional: cap on primitives returned, default 1000, at most 20000. 'count' still says " +
                         "how many matched.")]
            public int? Limit { get; set; }

            [Description("Only for an underlay inside a Revit link: its linkInstanceId from GetUnderlays. The id is " +
                         "then the element's id in the LINKED model. Omit for underlays of this model.")]
            public long? LinkInstanceId { get; set; }
        }
    }
}
