using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// Creates/recolors the <c>MC_*</c> Revit materials the block commands use. Optional — placing a
    /// block auto-creates its material — but this lets an AI (or the user) set up the whole palette
    /// up front or override colors ("make water more turquoise"). Existing materials are recolored
    /// in place, so already-placed blocks pick the change up immediately.
    /// </summary>
    [RevitCommand(
        Description = "Creates or recolors the MC_* materials used by the Minecraft block commands. Payload: " +
                      "{ entries: [{block, r, g, b, transparency (0-100, default 0)}], includeDefaults (default " +
                      "true — also ensure the built-in palette: grass, stone, dirt, glass, water…) }. " +
                      "Recoloring affects already-placed blocks too.",
        InputType = typeof(McSetPalette.Request))]
    internal sealed class McSetPalette : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                var doc = app.ActiveUIDocument.Document;

                using Autodesk.Revit.DB.Transaction t = new(doc, "Minecraft: set palette");
                t.Start();

                var materials = new BlockMaterialService(doc);
                var applied = new List<string>();

                if (req.IncludeDefaults)
                    foreach ((string block, BlockColor color) in BlockPalette.Defaults)
                    {
                        materials.CreateOrUpdate(block, color);
                        applied.Add(block);
                    }

                foreach (PaletteEntry entry in req.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Block)) continue;
                    materials.CreateOrUpdate(entry.Block.Trim(),
                        new BlockColor(entry.R, entry.G, entry.B, entry.Transparency));
                    applied.Add(entry.Block.Trim());
                }

                t.Commit();
                return new { ok = true, materials = applied };
            });
        }

        public sealed class Request
        {
            [JsonProperty("entries")]
            [Description("Custom palette entries; each creates or recolors material MC_<block>.")]
            public List<PaletteEntry> Entries { get; set; } = new();

            [JsonProperty("includeDefaults")]
            [Description("Also ensure the built-in palette exists (grass, stone, dirt, glass, water…). Default true.")]
            public bool IncludeDefaults { get; set; } = true;
        }

        public sealed record PaletteEntry
        {
            [JsonProperty("block")]
            [Description("Block type name, e.g. 'stone' or a custom one like 'marble'.")]
            public string Block { get; set; } = string.Empty;

            [JsonProperty("r")] public byte R { get; set; }
            [JsonProperty("g")] public byte G { get; set; }
            [JsonProperty("b")] public byte B { get; set; }

            [JsonProperty("transparency")]
            [Description("0 (opaque) to 100 (invisible). Use ~60 for glass/water-like blocks.")]
            public int Transparency { get; set; }
        }
    }
}
