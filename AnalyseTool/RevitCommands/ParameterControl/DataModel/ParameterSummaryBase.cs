using AnalyseTool.Extensions;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public record ParameterSummaryBase
    {
        public string CategoryName { get; set; }
        public string ParameterName { get; set; }
        public string Level { get; set; }
        public int ParameterCount { get; set; }
        public ParameterOrgin ParameterOrgin { get; set; }
        public bool IsTypeParameter { get; set; }
        public int ParameterEmpty { get; set; }
        public int ParameterFilled { get; set; }
        public double ParameterFilledProzent { get; set; }
        public IEnumerable<long> EmptyElements { get; set; }
        public IEnumerable<long> FilledElements { get; set; }
    }
}
