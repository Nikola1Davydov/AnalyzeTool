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

        /// <summary>Owning family, when the element has one — this is the join back to GetFamilies, which
        /// was impossible before: the two commands described the same families and shared no key.
        /// Null for system elements, whose types belong to a system family with no Family element.</summary>
        [JsonProperty("familyId", NullValueHandling = NullValueHandling.Ignore)]
        public long? FamilyId { get; init; }

        /// <summary>Family name, including the SYSTEM family name (e.g. "Basiswand") that has no
        /// <see cref="FamilyId"/>. Available far more often than the id, and it is what a person names.</summary>
        [JsonProperty("familyName", NullValueHandling = NullValueHandling.Ignore)]
        public string? FamilyName { get; init; }

        /// <summary>The element's type name. For a type, its own name; for an instance, its type's.</summary>
        [JsonProperty("typeName", NullValueHandling = NullValueHandling.Ignore)]
        public string? TypeName { get; init; }

        /// <summary>name -> value, only for the parameters the caller asked for. Omitted when none requested.</summary>
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string>? Parameters { get; init; }
    }

    /// <summary>
    /// What a category query answered, as an OBJECT rather than a bare array. Three things a bare array
    /// could not say, each of which was observed being guessed wrong:
    /// <list type="bullet">
    /// <item><see cref="Count"/> versus <see cref="Returned"/> — a truncated answer now says it is
    ///       truncated, instead of looking like the whole category.</item>
    /// <item><see cref="Error"/> — an unknown category is no longer an empty list, which reads exactly
    ///       like "this category is empty" and sent agents off building on nothing.</item>
    /// <item><see cref="ElementKind"/> — which of instances/types was actually answered.</item>
    /// </list>
    /// It also makes the result eligible for MCP structuredContent, which a top-level array can never be.
    /// </summary>
    public sealed record ElementsResult(
        [property: JsonProperty("category")] string? Category,
        [property: JsonProperty("elementKind")] string ElementKind,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("returned")] int Returned,
        [property: JsonProperty("elements")] IReadOnlyList<ElementSummary> Elements,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("didYouMean", NullValueHandling = NullValueHandling.Ignore)]
        IReadOnlyList<string>? DidYouMean);

    /// <summary>The parameters of a category query, kept as one object so the service signature stays
    /// readable and the command can fill it by name.</summary>
    public sealed class ElementQuery
    {
        public string? Category { get; init; }
        public string? BuiltInCategory { get; init; }
        public string? ElementKind { get; init; }
        public string? NameContains { get; init; }
        public string? FamilyNameContains { get; init; }
        public string? TypeNameContains { get; init; }
        public IReadOnlyCollection<string>? ParameterNames { get; init; }
        public int? Limit { get; init; }
    }

    /// <summary>What GetCategoryParameters answers: the resolved category (both names), its parameters,
    /// or an error with suggestions — an object for the same three reasons <see cref="ElementsResult"/>
    /// is one.</summary>
    public sealed record CategoryParametersResult(
        [property: JsonProperty("category")] string? Category,
        [property: JsonProperty("builtInCategory")] string? BuiltInCategory,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("parameters")] IReadOnlyList<CategoryParameterInfo> Parameters,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("didYouMean", NullValueHandling = NullValueHandling.Ignore)]
        IReadOnlyList<string>? DidYouMean);

    /// <summary>Parameter metadata for a category (discovery), so AI callers know which
    /// parameter names they can request and whether they are writable.</summary>
    public sealed record CategoryParameterInfo
    {
        /// <summary>The id SetDataToParameters takes: a negative BuiltInParameter value, or the
        /// ParameterElement's ElementId for shared and project parameters. Was missing while the write
        /// command's description said "ids come from GetCategoryParameters" — the workflow broke in the
        /// middle and callers dug the id out with ExecuteRevitCode.</summary>
        [JsonProperty("id")]
        public long Id { get; init; }

        /// <summary>The BuiltInParameter name (e.g. "ALL_MODEL_INSTANCE_COMMENTS") when the parameter is
        /// one, else null — language-independent where the name is localised.</summary>
        [JsonProperty("builtInParameter", NullValueHandling = NullValueHandling.Ignore)]
        public string? BuiltInParameter { get; init; }

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
