namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>Shading color + transparency for one block type. Values are 0-255 RGB and 0-100.</summary>
    internal readonly record struct BlockColor(byte R, byte G, byte B, int Transparency = 0);

    /// <summary>
    /// The built-in block palette: shading colors for the common Minecraft block types. Unknown
    /// block names still work — they get a deterministic color derived from the name, so an AI can
    /// invent block types freely and each one stays visually distinct and stable across runs.
    /// </summary>
    internal static class BlockPalette
    {
        public static readonly IReadOnlyDictionary<string, BlockColor> Defaults =
            new Dictionary<string, BlockColor>(StringComparer.OrdinalIgnoreCase)
            {
                ["grass"] = new(107, 142, 35),
                ["dirt"] = new(134, 96, 67),
                ["stone"] = new(125, 125, 125),
                ["cobblestone"] = new(100, 100, 100),
                ["bedrock"] = new(60, 60, 60),
                ["sand"] = new(219, 207, 163),
                ["gravel"] = new(136, 126, 126),
                ["oak_planks"] = new(162, 130, 78),
                ["oak_log"] = new(102, 81, 50),
                ["leaves"] = new(60, 120, 40),
                ["glass"] = new(200, 230, 240, 70),
                ["water"] = new(60, 90, 200, 55),
                ["lava"] = new(230, 110, 20),
                ["brick"] = new(150, 60, 50),
                ["stone_brick"] = new(110, 110, 110),
                ["snow"] = new(240, 240, 250),
                ["ice"] = new(160, 190, 240, 40),
                ["obsidian"] = new(25, 18, 40),
                ["wool_white"] = new(235, 235, 235),
                ["wool_red"] = new(180, 50, 50),
                ["wool_blue"] = new(55, 75, 170),
                ["wool_yellow"] = new(220, 200, 60),
                ["wool_green"] = new(80, 130, 60),
                ["wool_black"] = new(35, 35, 35),
                ["gold"] = new(250, 215, 90),
                ["iron"] = new(215, 215, 215),
                ["diamond"] = new(120, 230, 220),
            };

        /// <summary>Color for a block type: palette entry when known, otherwise a stable color hashed
        /// from the name (kept in a mid-brightness band so nothing renders near-black or washed out).</summary>
        public static BlockColor ColorFor(string block)
        {
            if (Defaults.TryGetValue(block, out BlockColor known)) return known;

            // FNV-1a — deterministic across runs and machines (string.GetHashCode is neither).
            uint h = 2166136261;
            foreach (char c in block.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            byte Channel(int shift) => (byte)(70 + ((h >> shift) & 0xFF) % 150);
            return new BlockColor(Channel(0), Channel(8), Channel(16));
        }
    }
}
