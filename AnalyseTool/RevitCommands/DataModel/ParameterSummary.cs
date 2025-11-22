namespace AnalyseTool.RevitCommands.DataModel
{
    public sealed record ParameterSummary : ParameterSummaryBase
    {

        public List<ParameterSummaryBase> ChildParameters { get; set; } = new List<ParameterSummaryBase>();

    }
}
