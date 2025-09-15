using AnalyseTool.Extensions;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public sealed record ParameterSummary
    {
        public string CategoryName { get; set; }
        public string ParameterName { get; set; }
        public int ParameterCount { get; set; }
        public int ParameterEmpty { get; set; }
        public int ParameterFilled { get; set; }
        public ParameterOrgin ParameterOrgin { get; set; }
        public double ParameterFilledProzent { get; set; }
        public IEnumerable<long> EmptyElements { get; set; }
        public IEnumerable<long> FilledElements { get; set; }
        public List<ParameterSummary> ChildParameters { get; set; }

        public void AddChild(ParameterSummary child)
        {
            ChildParameters.Add(child);
        }
    }
}
