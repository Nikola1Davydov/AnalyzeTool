using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using Autodesk.Revit.DB;

namespace AnalyseTool.Utils
{
    internal class DataElementsCollectorUtils
    {
        public static IEnumerable<DataElement> GetAllElements(Document doc)
        {
            FilteredElementCollector collectorInstances = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            FilteredElementCollector collectorTypes = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            FilteredElementCollector collector = collectorInstances.UnionWith(collectorTypes);

            IList<Element> elements = collector.ToElements();

            return elements.Select(x => new DataElement(x));
        }
    }
}
