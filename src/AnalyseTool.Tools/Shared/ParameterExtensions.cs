using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using System.Globalization;

namespace AnalyseTool.Tools.Shared
{
    internal static class ParameterExtensions
    {
        public static ParameterOrigin GetParameterOrigin(this Parameter parameter)
        {
            ParameterOrigin result = ParameterOrigin.BuiltIn;
            if (parameter.Id.Value > -1)
            {
                result = ParameterOrigin.Project;
            }
            if (parameter.IsShared)
            {
                result = ParameterOrigin.Shared;
            }
            return result;
        }
        public static string GetParameterValue(this Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    return GetDoubleParameterValue(parameter);
                case StorageType.Integer:
                    return parameter.AsInteger().ToString();
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;
                case StorageType.ElementId:
                    return parameter.AsElementId().Value.ToString();
                default:
                    return string.Empty;
            }
        }

        public static void SetParameterValue(this Parameter parameter, string value)
        {
            string paramName = parameter.Definition.Name;
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    string normalizedValue = value.Trim().Replace(',', '.');
                    if (!double.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
                        throw new ArgumentException($"Parameter '{paramName}': cannot convert '{value}' to Double.");
                    parameter.Set(GetDoubleInternalValue(parameter, doubleValue));
                    break;
                case StorageType.Integer:
                    if (!int.TryParse(value, out int intValue))
                        throw new ArgumentException($"Parameter '{paramName}': cannot convert '{value}' to Integer.");
                    parameter.Set(intValue);
                    break;
                case StorageType.String:
                    parameter.Set(value);
                    break;
                case StorageType.ElementId:
                    if (!int.TryParse(value, out int elementIdValue))
                        throw new ArgumentException($"Parameter '{paramName}': cannot convert '{value}' to ElementId.");
                    parameter.Set(new ElementId(elementIdValue));
                    break;
            }
        }

        private static string GetDoubleParameterValue(Parameter parameter)
        {
            double value = parameter.AsDouble();
            ForgeTypeId unitTypeId = GetProjectUnitTypeId(parameter);

            if (unitTypeId != null)
            {
                value = UnitUtils.ConvertFromInternalUnits(value, unitTypeId);
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static double GetDoubleInternalValue(Parameter parameter, double value)
        {
            ForgeTypeId unitTypeId = GetProjectUnitTypeId(parameter);
            return unitTypeId == null ? value : UnitUtils.ConvertToInternalUnits(value, unitTypeId);
        }

        /// <summary>
        /// What a parameter's value MEANS: its spec ("length", "area", "angle", "number", "string"…) and,
        /// for a measurable spec, the document's display unit for it ("millimeters", "squareMeters"…) — which
        /// is the unit every value this assembly reads or writes is expressed in (see GetDoubleParameterValue).
        /// An agent seeing "Höhe": "2400" had nothing that said millimetres; now it has (#113). Both are the
        /// short form of Revit's ForgeTypeId ("autodesk.spec.aec:length-2.0.0" → "length"), language-independent.
        /// Unit is null for a non-measurable spec; spec is null only when the definition carries no data type.
        /// </summary>
        public static (string? Spec, string? Unit) DescribeUnits(this Parameter parameter)
        {
            ForgeTypeId? specTypeId = parameter.Definition?.GetDataType();
            if (specTypeId == null || string.IsNullOrEmpty(specTypeId.TypeId)) return (null, null);
            string spec = ShortForgeId(specTypeId);
            if (!UnitUtils.IsMeasurableSpec(specTypeId)) return (spec, null);
            ForgeTypeId? unit = parameter.Element.Document.GetUnits().GetFormatOptions(specTypeId)?.GetUnitTypeId();
            return (spec, unit == null || string.IsNullOrEmpty(unit.TypeId) ? null : ShortForgeId(unit));
        }

        /// <summary>"autodesk.spec.aec:length-2.0.0" → "length"; "autodesk.spec:spec.string-2.0.0" → "string";
        /// "autodesk.unit.unit:millimeters-1.0.1" → "millimeters". The part after the last ':' up to the
        /// version, minus the "spec." prefix the plain data types carry.</summary>
        internal static string ShortForgeId(ForgeTypeId id)
        {
            string s = id.TypeId;
            int colon = s.LastIndexOf(':');
            if (colon >= 0) s = s[(colon + 1)..];
            int dash = s.IndexOf('-');
            if (dash > 0) s = s[..dash];
            return s.StartsWith("spec.", StringComparison.Ordinal) ? s[5..] : s;
        }

        private static ForgeTypeId GetProjectUnitTypeId(Parameter parameter)
        {
            ForgeTypeId specTypeId = parameter.Definition.GetDataType();
            if (!UnitUtils.IsMeasurableSpec(specTypeId))
            {
                return null;
            }

            Units units = parameter.Element.Document.GetUnits();
            FormatOptions formatOptions = units.GetFormatOptions(specTypeId);
            return formatOptions?.GetUnitTypeId();
        }
    }
}
