namespace AnalyseTool.RevitCommands.Model
{
    public record WarningInRevitModel
    {
        public string WarningDescription { get; set; }
        public List<long> FailingElements { get; set; }
        public List<long> AdditionalElements { get; set; }
    }
}
