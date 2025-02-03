namespace AnalyseTool.ParameterControl.Models
{
    public interface IAnalyseToolModel
    {
        List<ParameterDefinition> AnalyzeData();
        void SelectElements(IList<ElementId> elementIds);
    }
}