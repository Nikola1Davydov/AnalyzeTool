using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.Model
{
    public sealed class AnalyzeParameterWithAiRequest()
    {
        [JsonProperty("items")]
        public List<ParameterData> Items { get; set; } = new();

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; } = "gemma4:latest";
    }
}
