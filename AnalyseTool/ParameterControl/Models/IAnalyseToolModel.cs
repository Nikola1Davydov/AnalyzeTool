using Autodesk.Revit.DB;

namespace AnalyseTool.ParameterControl.Models
{
    public interface IAnalyseToolModel
    {
        List<ParameterDefinition> AnalyzeData();
        void SelectElements(IList<ElementId> elementIds);
    }
}