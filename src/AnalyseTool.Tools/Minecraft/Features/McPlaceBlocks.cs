using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// The basic Minecraft-style building brick for AI agents: place a list of block cubes at integer
    /// cell coordinates. Each block is a cube family instance with its material set from the block
    /// type (materials are created on demand — unknown types get a stable auto color). For solid
    /// volumes and walls use <see cref="McFillRegion"/> instead of listing thousands of cells.
    /// </summary>
    [RevitCommand(
        Description = "Places Minecraft-style block cubes at integer cells. Payload: { blocks: [{x,y,z,block}], " +
                      "blockSizeMeters (default 1) }. y is UP. Block types: grass, stone, dirt, oak_planks, glass, " +
                      "water, wool_red… (unknown names get a stable auto color). For boxes/walls prefer McFillRegion. " +
                      "Modifies the model; remove blocks with McClearRegion.",
        InputType = typeof(McPlaceBlocks.Request))]
    internal sealed class McPlaceBlocks : IRevitTask, IProgressAware
    {
        /// <summary>Hard cap per call — protects Revit from an AI enthusiastically flooding the model.</summary>
        internal const int MaxBlocksPerCall = 30_000;

        public IProgress<ProgressInfo>? Progress { get; set; }

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (req.Blocks.Count == 0)
                return Task.FromResult<object?>(new { ok = false, error = "No blocks in payload." });
            if (req.Blocks.Count > MaxBlocksPerCall)
                return Task.FromResult<object?>(new { ok = false, error = $"Too many blocks ({req.Blocks.Count}); max {MaxBlocksPerCall} per call." });
            if (req.BlockSizeMeters <= 0)
                return Task.FromResult<object?>(new { ok = false, error = "blockSizeMeters must be positive." });

            List<BlockPlacement> placements = req.Blocks
                .Where(b => !string.IsNullOrWhiteSpace(b.Block))
                .Select(b => new BlockPlacement(b.X, b.Y, b.Z, b.Block.Trim()))
                .ToList();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                var world = new BlockWorldService(app.ActiveUIDocument.Document, req.BlockSizeMeters / 0.3048, req.FamilyName);
                BlockPlaceResult built = world.PlaceBlocks(app, placements,
                    (fraction, message) => Progress?.Report(new ProgressInfo(fraction, message)), ct);

                return new
                {
                    ok = built.Error == null,
                    error = built.Error,
                    created = built.Created,
                    failed = built.Failed,
                    mode = built.Mode,
                    warnings = built.Warnings
                };
            });
        }

        public sealed class Request
        {
            [JsonProperty("blocks")]
            [Description("Blocks to place, each { x, y, z, block }. y is UP (Minecraft convention).")]
            public List<BlockDto> Blocks { get; set; } = new();

            [JsonProperty("blockSizeMeters")]
            [Description("Edge length of one block in meters. Default 1 (Minecraft scale).")]
            public double BlockSizeMeters { get; set; } = 1.0;

            [JsonProperty("familyName")]
            [Description("Optional: place instances of THIS loaded cube family (e.g. 'McCube') instead of the " +
                         "auto-generated one. Each block type becomes a duplicated family type with its material " +
                         "set on the type's material parameter. Insertion point: bottom center of the cell.")]
            public string? FamilyName { get; set; }
        }

        public sealed record BlockDto
        {
            [JsonProperty("x")] public int X { get; set; }

            [JsonProperty("y")]
            [Description("UP axis (Minecraft convention).")]
            public int Y { get; set; }

            [JsonProperty("z")] public int Z { get; set; }

            [JsonProperty("block")]
            [Description("Block type, e.g. 'stone', 'grass', 'oak_planks', 'glass', 'wool_red'.")]
            public string Block { get; set; } = "stone";
        }
    }
}
