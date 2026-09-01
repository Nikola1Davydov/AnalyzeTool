namespace AnalyseTool.Tools.Dwg
{
    /// <summary>
    /// Drawing units to Revit's internal unit, which is decimal feet.
    ///
    /// The conversion is arithmetic rather than a call into <c>UnitUtils</c> on purpose: the factor is
    /// exact and fixed by definition (the international foot has been 0.3048 m since 1959), while the
    /// unit-id route needs a <c>ForgeTypeId</c> per unit and offers none for several INSUNITS values.
    ///
    /// This is the single most consequential number in a DWG import. INSUNITS = 0 (UNITLESS) is common,
    /// and nothing in the file says whether its numbers are millimetres or metres — so it is never
    /// guessed here. The caller has to ask.
    /// </summary>
    internal static class DwgUnits
    {
        private const double MetersPerFoot = 0.3048;

        /// <summary>Metres per one drawing unit, by unit name. The names match what the sidecar reports
        /// for INSUNITS, plus the short forms a person would type.</summary>
        private static readonly IReadOnlyDictionary<string, double> MetersPerUnit =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["millimeters"] = 0.001,
                ["millimetres"] = 0.001,
                ["mm"] = 0.001,
                ["centimeters"] = 0.01,
                ["centimetres"] = 0.01,
                ["cm"] = 0.01,
                ["decimeters"] = 0.1,
                ["decimetres"] = 0.1,
                ["dm"] = 0.1,
                ["meters"] = 1.0,
                ["metres"] = 1.0,
                ["m"] = 1.0,
                ["kilometers"] = 1000.0,
                ["kilometres"] = 1000.0,
                ["km"] = 1000.0,
                ["inches"] = 0.0254,
                ["inch"] = 0.0254,
                ["in"] = 0.0254,
                ["feet"] = MetersPerFoot,
                ["foot"] = MetersPerFoot,
                ["ft"] = MetersPerFoot,
                ["yards"] = 0.9144,
                ["yd"] = 0.9144,
                ["miles"] = 1609.344,
                ["mi"] = 1609.344,
            };

        /// <summary>INSUNITS code to unit name, for the codes that name a length this tool can convert.
        /// Everything else (unitless, microinches, parsecs) is absent on purpose — a drawing measured in
        /// light years is not one someone is importing into Revit by accident.</summary>
        private static readonly IReadOnlyDictionary<int, string> UnitByCode = new Dictionary<int, string>
        {
            [1] = "inches",
            [2] = "feet",
            [3] = "miles",
            [4] = "millimeters",
            [5] = "centimeters",
            [6] = "meters",
            [7] = "kilometers",
            [10] = "yards",
            [14] = "decimeters",
        };

        /// <summary>The names <see cref="TryGetFeetPerUnit"/> accepts, for an error message that tells the
        /// caller what to send instead of leaving them guessing.</summary>
        public static string SupportedNames =>
            string.Join(", ", new[] { "millimeters", "centimeters", "decimeters", "meters", "kilometers", "inches", "feet", "yards", "miles" });

        /// <summary>Revit internal feet per one drawing unit of the named unit.</summary>
        public static bool TryGetFeetPerUnit(string? unitName, out double feetPerUnit)
        {
            feetPerUnit = 0;
            if (string.IsNullOrWhiteSpace(unitName)) return false;
            if (!MetersPerUnit.TryGetValue(unitName!.Trim(), out double meters)) return false;

            feetPerUnit = meters / MetersPerFoot;
            return true;
        }

        /// <summary>The unit name for an INSUNITS code, or null when the file does not state a usable one
        /// — which includes the very common code 0, UNITLESS.</summary>
        public static string? NameForCode(int code) => UnitByCode.TryGetValue(code, out string? name) ? name : null;
    }
}
