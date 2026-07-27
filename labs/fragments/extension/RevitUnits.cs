using System.Globalization;
using Autodesk.Revit.DB;

namespace AnalyseTool.Fragments.Revit
{
    /// <summary>
    /// Converts parameter values into something two models can actually be compared by.
    /// <para>
    /// The trap is <c>Parameter.AsValueString()</c>: it formats in the PROJECT's display units and in
    /// the Revit UI's language. The same wall thickness comes out as "200", "0.2", "7 7/8&quot;" or
    /// "0,2" depending on the project and the installed language — none of which can be diffed
    /// against an IFC file, and the last of which cannot even be parsed as a number.
    /// </para>
    /// <para>
    /// So measurable values are written as invariant-culture numbers in SI, converted from Revit's
    /// internal units, with the unit named in the attribute's type. What the project happens to
    /// DISPLAY is recorded once in the model metadata, where a UI can pick it up for formatting —
    /// that is presentation, not identity.
    /// </para>
    /// <para>
    /// Geometry needs none of this: Revit's internal length unit is decimal feet in every project,
    /// whatever the display units are, so the exporter's feet→metres factor is already
    /// project-independent.
    /// </para>
    /// </summary>
    internal sealed class RevitUnits
    {
        private readonly Document _document;

        /// <summary>
        /// Cache keyed by spec: both the unit and its label are fixed per spec, and resolving either
        /// is an API call. A large model runs this hundreds of thousands of times.
        /// </summary>
        private readonly Dictionary<string, (ForgeTypeId? Unit, string Type)> _specs = new();

        internal RevitUnits(Document document)
        {
            _document = document;
        }

        /// <summary>
        /// Formats a parameter for export: an invariant number in SI where the value is measurable,
        /// the raw text otherwise. Returns null when there is nothing worth writing.
        /// </summary>
        internal (string Value, string Type)? Format(Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    double raw = parameter.AsDouble();
                    (ForgeTypeId? unit, string type) = SpecInfo(parameter.Definition.GetDataType());

                    // No SI mapping: export Revit's internal value and SAY it is internal, rather
                    // than converting to an arbitrary unit or implying metres.
                    return unit is null
                        ? (Invariant(raw), type)
                        : (Invariant(UnitUtils.ConvertFromInternalUnits(raw, unit)), type);

                case StorageType.Integer:
                    // Yes/No parameters are integers; keep them readable rather than 0/1.
                    ForgeTypeId intSpec = parameter.Definition.GetDataType();
                    if (intSpec == SpecTypeId.Boolean.YesNo)
                        return (parameter.AsInteger() != 0 ? "true" : "false", "Boolean");

                    return (parameter.AsInteger().ToString(CultureInfo.InvariantCulture), "Integer");

                case StorageType.String:
                    string? text = parameter.AsString();
                    return string.IsNullOrEmpty(text) ? null : (text!, "Text");

                case StorageType.ElementId:
                    long id = parameter.AsElementId().Value;
                    return id == -1 ? null : (id.ToString(CultureInfo.InvariantCulture), "ElementId");

                default:
                    return null;
            }
        }

        /// <summary>
        /// What the project DISPLAYS its quantities in — for a UI to format with, never for storage.
        /// </summary>
        internal Dictionary<string, string> DisplayUnits()
        {
            Dictionary<string, string> units = new();
            Units documentUnits = _document.GetUnits();

            foreach ((string name, ForgeTypeId spec) in new[]
            {
                ("length", SpecTypeId.Length),
                ("area", SpecTypeId.Area),
                ("volume", SpecTypeId.Volume),
                ("angle", SpecTypeId.Angle),
            })
            {
                try
                {
                    units[name] = Token(documentUnits.GetFormatOptions(spec).GetUnitTypeId());
                }
                catch
                {
                    /* a spec the document has no format options for — skip it */
                }
            }

            return units;
        }

        /// <summary>The unit a spec is exported in (null = Revit internal) and its attribute type.</summary>
        private (ForgeTypeId? Unit, string Type) SpecInfo(ForgeTypeId spec)
        {
            if (spec is null || spec.Empty()) return (null, "Number");

            string key = spec.TypeId;
            if (_specs.TryGetValue(key, out (ForgeTypeId? Unit, string Type) cached)) return cached;

            ForgeTypeId? unit = Resolve(spec);
            string type = unit is null ? $"{Token(spec)}[internal]" : TypeNames[unit.TypeId];

            (ForgeTypeId? Unit, string Type) info = (unit, type);
            _specs[key] = info;
            return info;
        }

        /// <summary>
        /// The specs worth pinning explicitly — these are what a geometric comparison turns on, and
        /// IFC expresses them in exactly these units. Angle is already radians internally, so that
        /// conversion is the identity; it is named anyway so the attribute says what it is.
        /// <para>
        /// Everything else deliberately returns null and is exported in Revit's internal units,
        /// labelled as such. Picking, say, <c>GetValidUnits(spec).First()</c> would be an arbitrary
        /// choice that could differ between Revit versions — and a value whose unit silently changes
        /// between two exports is worse than one that never converts at all, given the whole point
        /// here is diffing two models.
        /// </para>
        /// </summary>
        private static ForgeTypeId? Resolve(ForgeTypeId spec)
        {
            if (spec == SpecTypeId.Length) return UnitTypeId.Meters;
            if (spec == SpecTypeId.Area) return UnitTypeId.SquareMeters;
            if (spec == SpecTypeId.Volume) return UnitTypeId.CubicMeters;
            if (spec == SpecTypeId.Angle) return UnitTypeId.Radians;
            return null;
        }

        /// <summary>
        /// Attribute type names, written out rather than taken from <c>LabelUtils</c>: those labels are
        /// LOCALIZED, so a German Revit would emit "Länge[m]" and the type itself would stop being
        /// comparable across machines — the very problem this class exists to remove.
        /// </summary>
        private static readonly Dictionary<string, string> TypeNames = new()
        {
            [UnitTypeId.Meters.TypeId] = "Length[m]",
            [UnitTypeId.SquareMeters.TypeId] = "Area[m2]",
            [UnitTypeId.CubicMeters.TypeId] = "Volume[m3]",
            [UnitTypeId.Radians.TypeId] = "Angle[rad]",
        };

        /// <summary>
        /// The readable middle of a ForgeTypeId — "autodesk.spec.aec:mass-2.0.0" becomes "mass".
        /// Language-independent, unlike the label APIs.
        /// </summary>
        private static string Token(ForgeTypeId id)
        {
            string typeId = id.TypeId;
            int colon = typeId.IndexOf(':');
            if (colon < 0) return typeId;

            int dash = typeId.IndexOf('-', colon);
            return dash < 0 ? typeId.Substring(colon + 1) : typeId.Substring(colon + 1, dash - colon - 1);
        }

        /// <summary>"R" keeps the round-trip exact; invariant culture keeps the separator a dot.</summary>
        private static string Invariant(double value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

    }
}
