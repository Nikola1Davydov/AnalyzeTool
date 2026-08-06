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

    /// <summary>
    /// One parameter of one element — the row a validation pipeline checks, reports and fixes.
    ///
    /// <para><c>elementId</c> and <c>id</c> are named for what consumes them: <c>id</c> is the
    /// PARAMETER id, so a row can be bound straight into <c>SetDataToParameters</c>, whose items are
    /// <c>{ elementId, id, value }</c>. That naming is already the convention in <c>ParameterData</c>,
    /// and breaking it here would mean a mapping node in the middle of every fix pipeline.</para>
    ///
    /// <para><c>elementName</c> and <c>category</c> ride along because a finding has to be readable by
    /// whoever receives it: "Wall 12345" is not something anyone can act on.</para>
    /// </summary>
    public sealed record ParameterRow(
        [property: JsonProperty("elementId")] long ElementId,
        [property: JsonProperty("elementName")] string ElementName,
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("level")] string Level,
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("value")] string Value,
        [property: JsonProperty("storageType")] string StorageType,
        [property: JsonProperty("isReadOnly")] bool IsReadOnly,
        [property: JsonProperty("isTypeParameter")] bool IsTypeParameter,
        /// <summary>False when the element does not have this parameter at all — which is a finding,
        /// not a reason to omit the row. A row that is not there cannot be reported.</summary>
        [property: JsonProperty("found")] bool Found);

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
