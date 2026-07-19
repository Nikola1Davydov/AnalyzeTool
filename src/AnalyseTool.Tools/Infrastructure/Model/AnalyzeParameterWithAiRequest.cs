using Newtonsoft.Json;

namespace AnalyseTool.Tools.Infrastructure.Model
{
    public sealed class AnalyzeParameterWithAiRequest()
    {
        [JsonProperty("items")]
        public List<ParameterData> Items { get; set; } = new();

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; } = "gemma4:latest";

        [JsonProperty("provider")]
        public string? Provider { get; set; } // null = built-in local Ollama (back-compat)
    }
}
