using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions
{
    public enum ParameterOrgin
    {
        Shared,
        Project,
        BuiltIn
    }
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
    }
}
