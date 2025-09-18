namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public sealed record ParameterSummary : ParameterSummaryBase
    {

        public List<ParameterSummaryBase> ChildParameters { get; set; } = new List<ParameterSummaryBase>();

    }
}
