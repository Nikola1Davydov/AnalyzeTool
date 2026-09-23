using AnalyseTool.Sdk;
using AnalyseTool.Sdk.Underlays;

namespace Acme.Sample
{
    /// <summary>
    /// Sample of reusing the platform's underlay reading (AnalyseTool.Sdk.Underlays) instead of walking
    /// ImportInstance geometry by hand: finds the grid layer of every loaded DWG by name and returns its
    /// lines in project coordinates (mm) — the first step of "build grids from the architect's DWG".
    /// Registered as "acme.sample.DwgGridLines".
    /// </summary>
    [RevitCommand("DwgGridLines",
        Description = "Returns the straight lines on every DWG layer whose name contains 'GRID' (or the " +
                      "given text), in project coordinates, mm. Read-only.",
        ReadOnly = true,
        InputType = typeof(DwgGridLines.Input))]
    public sealed class DwgGridLines : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
        {
            Input input = revitContext.Payload.As<Input>() ?? new Input();
            string pattern = string.IsNullOrWhiteSpace(input.LayerContains) ? "GRID" : input.LayerContains!;

            return revitContext.RunInRevitAsync<object?>(app =>
            {
                Autodesk.Revit.DB.Document doc = app.ActiveUIDocument.Document;
                UnderlaysResult underlays = UnderlayReader.GetUnderlays(doc, kinds: new[] { "dwg" });

                return underlays.Underlays.Select(dwg =>
                {
                    string[] gridLayers = (dwg.Layers ?? Array.Empty<CadLayerSummary>())
                        .Where(l => l.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Name)
                        .ToArray();
                    CadGeometryResult lines = gridLayers.Length == 0
                        ? new CadGeometryResult { ImportId = dwg.Id }
                        : UnderlayReader.GetCadGeometry(doc, dwg.Id, layers: gridLayers, types: new[] { "line" });
                    return new { dwg = dwg.Name, layers = gridLayers, lines = lines.Primitives };
                }).ToList();
            });
        }

        public sealed record Input
        {
            [System.ComponentModel.Description("Optional text the grid layer's name contains; defaults to \"GRID\".")]
            public string? LayerContains { get; set; }
        }
    }
}
