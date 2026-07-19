using Autodesk.Revit.UI;

namespace AnalyseTool.Core.Common.Utils
{
    internal class CommonUtils
    {
        internal static string HostRevitTag(string versionNumber) => versionNumber.Length >= 4 ? $"R{versionNumber.Substring(2)}" : versionNumber;
    }
}
