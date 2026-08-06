using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Tools.Ids
{
    /// <summary>
    /// Which IFC class a Revit element counts as.
    ///
    /// <para><b>This is the approximate part of the whole feature, and it is deliberately a file.</b>
    /// IDS speaks IFC — <c>IFCWALL</c>, <c>IFCDOOR</c> — and Revit speaks categories. The real answer
    /// depends on the IFC export settings of the project, which live outside this document, so no
    /// table compiled into an assembly can be right for everyone. An office whose export maps
    /// generic models to something specific has to be able to say so without waiting for a release.</para>
    ///
    /// <para>Revit's own per-element override wins over the table: an element carrying <c>IfcExportAs</c>
    /// is telling us exactly what it will become, and that is better evidence than any category
    /// mapping. The dotted form <c>IFCWALL.SOLIDWALL</c> also carries the predefined type.</para>
    ///
    /// <para>An unmapped category yields an empty class, and an entity with no class matches no
    /// <c>entity</c> facet — so such elements fall out of every specification's applicability rather
    /// than being checked against the wrong rule. That is visible in the run as an applicable count
    /// lower than expected, which is the honest failure: better a rule that says "I did not look at
    /// these" than one that judges a beam by a wall's requirements.</para>
    /// </summary>
    internal sealed class IfcClassMap
    {
        private readonly Dictionary<string, string> _byCategory;

        private IfcClassMap(Dictionary<string, string> byCategory) => _byCategory = byCategory;

        /// <summary>The built-in table, by BuiltInCategory name so it is language-independent — a
        /// category's DISPLAY name is German on a German Revit, and a table keyed by it would work on
        /// one machine and silently match nothing on the next.</summary>
        private static readonly Dictionary<string, string> Default = new(StringComparer.OrdinalIgnoreCase)
        {
            ["OST_Walls"] = "IFCWALL",
            ["OST_Floors"] = "IFCSLAB",
            ["OST_Roofs"] = "IFCROOF",
            ["OST_Ceilings"] = "IFCCOVERING",
            ["OST_Doors"] = "IFCDOOR",
            ["OST_Windows"] = "IFCWINDOW",
            ["OST_Columns"] = "IFCCOLUMN",
            ["OST_StructuralColumns"] = "IFCCOLUMN",
            ["OST_StructuralFraming"] = "IFCBEAM",
            ["OST_StructuralFoundation"] = "IFCFOOTING",
            ["OST_Stairs"] = "IFCSTAIR",
            ["OST_Ramps"] = "IFCRAMP",
            ["OST_StairsRailing"] = "IFCRAILING",
            ["OST_Railings"] = "IFCRAILING",
            ["OST_CurtainWallPanels"] = "IFCPLATE",
            ["OST_CurtainWallMullions"] = "IFCMEMBER",
            ["OST_Furniture"] = "IFCFURNITURE",
            ["OST_PlumbingFixtures"] = "IFCSANITARYTERMINAL",
            ["OST_MechanicalEquipment"] = "IFCUNITARYEQUIPMENT",
            ["OST_ElectricalEquipment"] = "IFCELECTRICAPPLIANCE",
            ["OST_ElectricalFixtures"] = "IFCELECTRICAPPLIANCE",
            ["OST_LightingFixtures"] = "IFCLIGHTFIXTURE",
            ["OST_DuctCurves"] = "IFCDUCTSEGMENT",
            ["OST_DuctFitting"] = "IFCDUCTFITTING",
            ["OST_PipeCurves"] = "IFCPIPESEGMENT",
            ["OST_PipeFitting"] = "IFCPIPEFITTING",
            ["OST_Rooms"] = "IFCSPACE",
            ["OST_MEPSpaces"] = "IFCSPACE",
            ["OST_Levels"] = "IFCBUILDINGSTOREY",
            ["OST_Grids"] = "IFCGRIDAXIS",
            ["OST_GenericModel"] = "IFCBUILDINGELEMENTPROXY",
            ["OST_Casework"] = "IFCFURNITURE",
            ["OST_SpecialityEquipment"] = "IFCBUILDINGELEMENTPROXY",
        };

        /// <summary>Loads the table, layering a user file over the built-in one so an office overrides
        /// only what it needs and still gets new defaults.</summary>
        public static IfcClassMap Load(string? path)
        {
            Dictionary<string, string> map = new(Default, StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(path)) return new IfcClassMap(map);

            if (!File.Exists(path))
                throw new InvalidOperationException(
                    $"There is no IFC class map at '{path}'. It is a JSON object of " +
                    "{ \"OST_Walls\": \"IFCWALL\" } pairs, keyed by BuiltInCategory name.");

            try
            {
                Dictionary<string, string>? overrides =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path!));

                foreach (KeyValuePair<string, string> entry in overrides ?? new Dictionary<string, string>())
                    map[entry.Key] = entry.Value;

                Log.Information("IFC class map: {Count} override(s) from {Path}", overrides?.Count ?? 0, path);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"The IFC class map at '{path}' is not readable JSON: {ex.Message}");
            }

            return new IfcClassMap(map);
        }

        /// <summary>
        /// The IFC class and predefined type for an element, or empty when nothing says.
        /// </summary>
        public (string IfcClass, string? PredefinedType) Resolve(Element element)
        {
            // The element's own word first. IfcExportAs is what the exporter will actually honour, so
            // a table that disagreed with it would be describing a model that never ships.
            string? exportAs = ReadParameter(element, "IfcExportAs");
            if (!string.IsNullOrWhiteSpace(exportAs))
            {
                string[] parts = exportAs!.Split('.');
                string declared = parts[0].Trim().ToUpperInvariant();

                // "DontExport" means the element is not in the delivery at all, so no specification
                // about the delivered model can be about it.
                if (string.Equals(declared, "DONTEXPORT", StringComparison.OrdinalIgnoreCase))
                    return (string.Empty, null);

                string? predefined = parts.Length > 1 ? parts[1].Trim().ToUpperInvariant() : null;
                return (declared, predefined ?? ReadPredefinedType(element));
            }

            BuiltInCategory builtIn = (BuiltInCategory)(element.Category?.Id.Value ?? 0);
            string key = builtIn.ToString();

            return _byCategory.TryGetValue(key, out string? mapped)
                ? (mapped, ReadPredefinedType(element))
                : (string.Empty, null);
        }

        private static string? ReadPredefinedType(Element element) =>
            ReadParameter(element, "IfcExportType") ?? ReadParameter(element, "IfcPredefinedType");

        /// <summary>Instance value, then the type's — the order Revit resolves in, and the order the
        /// exporter reads these overrides in.</summary>
        private static string? ReadParameter(Element element, string name)
        {
            Parameter? parameter = element.LookupParameter(name);
            if (parameter == null || !parameter.HasValue)
            {
                Element? type = element.Document.GetElement(element.GetTypeId());
                parameter = type?.LookupParameter(name);
            }

            string? value = parameter?.AsString();
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }
    }
}
