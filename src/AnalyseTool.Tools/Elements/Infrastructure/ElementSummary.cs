using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>Lean element projection for AI/MCP callers — element identity plus only the
    /// parameters that were explicitly requested. Keeps tool responses token-small.</summary>
    public sealed record ElementSummary
    {
        [JsonProperty("id")]
        public long Id { get; init; }

        [JsonProperty("name")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("category")]
        public string Category { get; init; } = string.Empty;

        [JsonProperty("level")]
        public string Level { get; init; } = string.Empty;

        [JsonProperty("isType")]
        public bool IsType { get; init; }

        /// <summary>name -> value, only for the parameters the caller asked for. Omitted when none requested.</summary>
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string>? Parameters { get; init; }
    }

    /// <summary>Parameter metadata for a category (discovery), so AI callers know which
    /// parameter names they can request and whether they are writable.</summary>
    public sealed record CategoryParameterInfo
    {
        [JsonProperty("name")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("storageType")]
        public string StorageType { get; init; } = string.Empty;

        [JsonProperty("isReadOnly")]
        public bool IsReadOnly { get; init; }

        [JsonProperty("isType")]
        public bool IsType { get; init; }
    }
}
