using AnalyseTool.Tools.Shared;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Ai
{
    public sealed class AnalyzeParameterWithAiRequest()
    {
        [JsonProperty("items")]
        [Description("The parameter rows to reason about: one entry per element/parameter pair, each " +
                     "carrying its current value and the metadata the model needs to judge it.")]
        public List<ParameterData> Items { get; set; } = new();

        [JsonProperty("prompt")]
        [Description("What to ask of the rows, in the user's own words — the model answers in the same " +
                     "language it is asked in.")]
        public string Prompt { get; set; }

        [JsonProperty("model")]
        [Description("Model name as the provider lists it, e.g. 'llama3.2:latest'. Get the valid names " +
                     "from AiGetModels for the provider you are about to use.")]
        // No default. There was one — "gemma4:latest" — and no such model exists (the line is
        // gemma/gemma2/gemma3), so a caller that omitted the field got a 404 from Ollama naming a
        // model it had never heard of. A required field that says so beats a default that lies.
        public string Model { get; set; } = string.Empty;

        [JsonProperty("provider")]
        [Description("Provider id from AiGetProviders; omit/null for the built-in local Ollama.")]
        public string? Provider { get; set; } // null = built-in local Ollama (back-compat)
    }
}
