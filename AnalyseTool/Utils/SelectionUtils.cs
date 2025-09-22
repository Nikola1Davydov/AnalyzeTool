using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Utils
{
    internal class SelectionUtils
    {
        public static void SelectElements(UIDocument uidoc, IEnumerable<long> elementIds)
        {
            List<ElementId> selectionList = elementIds.Select(x => new ElementId(x)).ToList();
            uidoc.Selection.SetElementIds(selectionList);
        }
    }
}
