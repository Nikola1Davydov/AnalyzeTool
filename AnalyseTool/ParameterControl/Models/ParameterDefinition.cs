using Autodesk.Revit.DB;

namespace AnalyseTool.ParameterControl.Models
{
    public partial class ParameterDefinition
    {
        public string Name { get; set; }
        public int ParameterEmpty { get; set; }
        public int ParameterCount { get; set; }
        public double ParameterFilledProzent { get; set; }
        public IEnumerable<ElementId> EmptyElements { get; set; }
        public IEnumerable<ElementId> FilledElements { get; set; }
        public List<ParameterDefinition> ChildParameters { get; set; }
        public string CategoriesString { get; set; }

        public int _parameterFilled;
        public int ParameterFilled
        {
            get => _parameterFilled;
            set => Math.Round((double)ParameterFilled / ParameterCount * 100, 2);
        }

        public void AddChild(ParameterDefinition child)
        {
            ChildParameters.Add(child);
        }
    }
}
