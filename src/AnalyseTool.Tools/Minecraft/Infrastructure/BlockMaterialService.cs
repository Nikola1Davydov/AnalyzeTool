using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// Maps block type names to Revit materials named <c>MC_&lt;block&gt;</c>, creating them on demand
    /// from the palette (or a stable hashed color for unknown blocks). Must be called inside an open
    /// transaction. Caches lookups per instance — one instance per command run.
    /// </summary>
    internal sealed class BlockMaterialService
    {
        private const string NamePrefix = "MC_";

        private readonly Document _doc;
        private readonly Dictionary<string, ElementId> _cache = new(StringComparer.OrdinalIgnoreCase);

        public BlockMaterialService(Document doc)
        {
            _doc = doc;
            foreach (Material m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                if (m.Name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
                    _cache[m.Name.Substring(NamePrefix.Length)] = m.Id;
        }

        public ElementId EnsureMaterial(string block) =>
            _cache.TryGetValue(block, out ElementId id) ? id : CreateOrUpdate(block, BlockPalette.ColorFor(block));

        /// <summary>Creates the material for a block, or recolors it when it already exists (palette edits).</summary>
        public ElementId CreateOrUpdate(string block, BlockColor color)
        {
            ElementId id = _cache.TryGetValue(block, out ElementId existing)
                ? existing
                : Material.Create(_doc, Sanitize(NamePrefix + block));

            if (_doc.GetElement(id) is Material material)
            {
                material.Color = new Color(color.R, color.G, color.B);
                material.Transparency = Math.Clamp(color.Transparency, 0, 100);
            }

            _cache[block] = id;
            return id;
        }

        /// <summary>Revit rejects these characters in element names.</summary>
        private static string Sanitize(string name)
        {
            foreach (char c in "\\:{}[]|;<>?`~")
                name = name.Replace(c, '_');
            return name;
        }
    }
}
