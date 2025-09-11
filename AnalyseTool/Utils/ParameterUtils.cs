using Autodesk.Revit.DB;

namespace AnalyseTool.Utils
{
    public static class ParameterUtils
    {
        public static string GetParameterValue(Parameter parameter)
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
                    return parameter.AsElementId().IntegerValue.ToString();
                default:
                    return string.Empty;
            }
        }
    }
}
