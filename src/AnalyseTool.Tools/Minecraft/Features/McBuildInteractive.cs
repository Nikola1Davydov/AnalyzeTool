using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System.ComponentModel;
using RevitCanceled = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// Minecraft for humans: an interactive click-to-build session. Each pick places one block cube
    /// of the chosen type at the clicked cell (snapped to the block grid); clicking on an existing
    /// block stacks the new one on top, exactly like in the game. Escape ends the session. Every
    /// block is its own transaction, so Ctrl+Z removes the LAST block, not the whole session.
    /// Backs the "Build" button of the Minecraft dock panel. Hidden from MCP — it blocks Revit in an
    /// interactive prompt; agents use McPlaceBlocks/McFillRegion instead.
    /// </summary>
    [RevitCommand(
        Description = "Starts interactive Minecraft-style building: every model click places one block of the " +
                      "given type, Escape finishes. Payload: { block, blockSizeMeters (default 1) }.",
        HiddenFromMcp = true,
        InputType = typeof(McBuildInteractive.Request))]
    internal sealed class McBuildInteractive : IRevitTask
    {
        /// <summary>Runaway guard — nobody legitimately clicks this many blocks in one session.</summary>
        private const int MaxBlocksPerSession = 5000;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (string.IsNullOrWhiteSpace(req.Block))
                return Task.FromResult<object?>(new { ok = false, error = "block is required." });
            if (req.BlockSizeMeters <= 0)
                return Task.FromResult<object?>(new { ok = false, error = "blockSizeMeters must be positive." });

            string block = req.Block.Trim();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                var world = new BlockWorldService(uidoc.Document, req.BlockSizeMeters / 0.3048, req.FamilyName);

                string? familyError = world.ValidateUserFamily();
                if (familyError != null) return new { ok = false, placed = 0, error = familyError };

                FamilySymbol? symbol = string.IsNullOrWhiteSpace(req.FamilyName) ? world.PrepareSymbol(app) : null;

                int placed = 0;
                try
                {
                    while (placed < MaxBlocksPerSession && !ct.IsCancellationRequested)
                    {
                        XYZ picked = uidoc.Selection.PickPoint($"Place '{block}' — Esc to finish");
                        (int x, int y, int z) = world.CellOf(picked);

                        // Clicked cell taken? Stack upwards — the Minecraft way.
                        int lift = 0;
                        while (world.IsOccupied(x, y, z) && lift++ < 64) y++;

                        if (world.TryPlaceSingle(symbol, new BlockPlacement(x, y, z, block)))
                            placed++;
                    }
                }
                catch (RevitCanceled)
                {
                    // Escape — the normal way to end the session, not an error.
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    return new
                    {
                        ok = false,
                        placed,
                        error = "Picking points needs a work plane — switch to a floor plan view " +
                                "(or set a work plane in the 3D view) and start again."
                    };
                }

                return new { ok = true, placed, block };
            });
        }

        public sealed class Request
        {
            [JsonProperty("block")]
            [Description("Block type to place, e.g. 'stone', 'grass', 'oak_planks'.")]
            public string Block { get; set; } = "stone";

            [JsonProperty("blockSizeMeters")]
            [Description("Edge length of one block in meters. Default 1.")]
            public double BlockSizeMeters { get; set; } = 1.0;

            [JsonProperty("familyName")]
            [Description("Optional: place instances of THIS loaded cube family (e.g. 'McCube') instead of the " +
                         "auto-generated one — see McPlaceBlocks.")]
            public string? FamilyName { get; set; }
        }
    }
}
