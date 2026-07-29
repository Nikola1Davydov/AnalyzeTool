using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// The pickaxe: deletes block cubes whose cells intersect the given box. Only elements this
    /// plugin placed (tagged <c>MC:</c> in their Comments parameter) are touched — the user's own
    /// model is never at risk, no matter how wild the region.
    /// </summary>
    [RevitCommand(
        Description = "Removes Minecraft-style blocks in a box of cells (corners inclusive). Only blocks placed " +
                      "by McPlaceBlocks/McFillRegion are deleted — never other model elements. Payload: " +
                      "{ from: {x,y,z}, to: {x,y,z}, blockSizeMeters (default 1) }. y is UP.",
        Destructive = true,
        InputType = typeof(McClearRegion.Request))]
    internal sealed class McClearRegion : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (req.From == null || req.To == null)
                return Task.FromResult<object?>(new { ok = false, error = "from and to corners are required." });
            if (req.BlockSizeMeters <= 0)
                return Task.FromResult<object?>(new { ok = false, error = "blockSizeMeters must be positive." });

            return ctx.RunInRevitAsync<object?>(app =>
            {
                var world = new BlockWorldService(app.ActiveUIDocument.Document, req.BlockSizeMeters / 0.3048);
                int deleted = world.ClearRegion(req.From.X, req.From.Y, req.From.Z, req.To.X, req.To.Y, req.To.Z);
                return new { ok = true, deleted };
            });
        }

        public sealed class Request
        {
            [JsonProperty("from")]
            [Description("One corner of the box (inclusive).")]
            public CellDto? From { get; set; }

            [JsonProperty("to")]
            [Description("Opposite corner of the box (inclusive).")]
            public CellDto? To { get; set; }

            [JsonProperty("blockSizeMeters")]
            [Description("Edge length of one block in meters — must match the size used when placing. Default 1.")]
            public double BlockSizeMeters { get; set; } = 1.0;
        }
    }
}
