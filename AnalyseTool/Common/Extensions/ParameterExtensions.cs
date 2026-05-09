using AnalyseTool.Infrastructure.Model;
using Autodesk.Revit.DB;
using System.Globalization;

namespace AnalyseTool.Common.Extensions
{
    internal static class ParameterExtensions
    {
        public static ParameterOrgin GetParameterOrgin(this Parameter parameter)
        {
            ParameterOrgin result = ParameterOrgin.BuiltIn;
            if (parameter.Id.Value > -1)
            {
                result = ParameterOrgin.Project;
            }
            if (parameter.IsShared)
            {
                result = ParameterOrgin.Shared;
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
                    return parameter.AsString();
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
