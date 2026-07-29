using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// Fills a box of cells with one block type — the bulk sibling of <see cref="McPlaceBlocks"/>,
    /// so an AI builds a wall or a platform with ONE call instead of thousands of block entries.
    /// <c>hollow</c> keeps only the shell (walls+floor+ceiling of the box), the classic way to raise
    /// a room or a building carcass in one go.
    /// </summary>
    [RevitCommand(
        Description = "Fills a box of cells (corners inclusive) with one Minecraft-style block type. Payload: " +
                      "{ from: {x,y,z}, to: {x,y,z}, block, hollow (default false — true keeps only the shell), " +
                      "blockSizeMeters (default 1) }. y is UP. Modifies the model; remove with McClearRegion.",
        InputType = typeof(McFillRegion.Request))]
    internal sealed class McFillRegion : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (req.From == null || req.To == null)
                return Task.FromResult<object?>(new { ok = false, error = "from and to corners are required." });
            if (string.IsNullOrWhiteSpace(req.Block))
                return Task.FromResult<object?>(new { ok = false, error = "block is required." });
            if (req.BlockSizeMeters <= 0)
                return Task.FromResult<object?>(new { ok = false, error = "blockSizeMeters must be positive." });

            (int x1, int x2) = MinMax(req.From.X, req.To.X);
            (int y1, int y2) = MinMax(req.From.Y, req.To.Y);
            (int z1, int z2) = MinMax(req.From.Z, req.To.Z);

            List<BlockPlacement> placements = new();
            string block = req.Block.Trim();
            for (int x = x1; x <= x2; x++)
                for (int y = y1; y <= y2; y++)
                    for (int z = z1; z <= z2; z++)
                    {
                        if (req.Hollow && x != x1 && x != x2 && y != y1 && y != y2 && z != z1 && z != z2)
                            continue;
                        if (placements.Count > McPlaceBlocks.MaxBlocksPerCall)
                            return Task.FromResult<object?>(new
                            {
                                ok = false,
                                error = $"Region has more than {McPlaceBlocks.MaxBlocksPerCall} blocks — " +
                                        "split it into several calls (or use hollow: true)."
                            });
                        placements.Add(new BlockPlacement(x, y, z, block));
                    }

            return ctx.RunInRevitAsync<object?>(app =>
            {
                var world = new BlockWorldService(app.ActiveUIDocument.Document, req.BlockSizeMeters / 0.3048);
                BlockPlaceResult built = world.PlaceBlocks(app, placements,
                    (fraction, message) => Progress?.Report(new ProgressInfo(fraction, message)), ct);

                return new
                {
                    ok = true,
                    created = built.Created,
                    failed = built.Failed,
                    mode = built.Mode,
                    warnings = built.Warnings
                };
            });
        }

        private static (int Min, int Max) MinMax(int a, int b) => a <= b ? (a, b) : (b, a);

        public sealed class Request
        {
            [JsonProperty("from")]
            [Description("One corner of the box (inclusive).")]
            public CellDto? From { get; set; }

            [JsonProperty("to")]
            [Description("Opposite corner of the box (inclusive).")]
            public CellDto? To { get; set; }

            [JsonProperty("block")]
            [Description("Block type to fill with, e.g. 'stone', 'oak_planks', 'glass'.")]
            public string Block { get; set; } = "stone";

            [JsonProperty("hollow")]
            [Description("True = only the shell of the box (room/building carcass); false = solid fill.")]
            public bool Hollow { get; set; }

            [JsonProperty("blockSizeMeters")]
            [Description("Edge length of one block in meters. Default 1.")]
            public double BlockSizeMeters { get; set; } = 1.0;
        }
    }
}
