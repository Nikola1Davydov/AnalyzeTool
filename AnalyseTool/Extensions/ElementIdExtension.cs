using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions
{
    internal static class ElementIdExtension
    {
#if DEBUG_R23
        public static long Value(this ElementId elementId)
        {
            return elementId.IntegerValue;
        }
#else
        public static long Value(this ElementId elementId)
        {
            return elementId.Value;
        }
#endif
    }
}
