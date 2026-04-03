using AnalyseTool.RevitCommands.Model;
using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions
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
                    return parameter.AsDouble().ToString();
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
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    if (double.TryParse(value, out double doubleValue))
                    {
                        parameter.Set(doubleValue);
                    }
                    break;
                case StorageType.Integer:
                    if (int.TryParse(value, out int intValue))
                    {
                        parameter.Set(intValue);
                    }
                    break;
                case StorageType.String:
                    parameter.Set(value);
                    break;
                case StorageType.ElementId:
                    if (int.TryParse(value, out int elementIdValue))
                    {
                        parameter.Set(new ElementId(elementIdValue));
                    }
                    break;
            }
        }
    }
}
