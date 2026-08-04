using Newtonsoft.Json;

namespace AnalyseTool.Tools.Ai
{
    // Result types for the AI commands. These matter more than their size suggests: they are the shape a
    // generic AI node will generalise (see #92 — that node is these commands with the prompt and the
    // output schema moved from C# into node configuration), so what they promise is worth pinning now.
    //
    // Note the pattern OllamaSuggestNames established and the others follow: a batch answer is keyed by
    // the CALLER's id, echoed back, so a result can be matched to the row it came from. An AI adds a
    // column; it never invents the table.
    //
    // Prefixed Ai* to stay clear of AiAnalysisService's nested NameSuggestion / AbbreviationEntry, which
    // are that service's own parsing types and live in this same namespace.
    //
    // camelCase spelled out: the wire is written by Newtonsoft (declared names by default) and the
    // schema published for OutputType is generated with Web defaults (camelCase).

    /// <summary>One suggested name, keyed by the id the caller sent in.</summary>
    public sealed record AiNameSuggestion(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name);

    /// <summary>Batch naming answer. <see cref="Error"/> is set instead of throwing, so a partial failure
    /// (no model configured, a timeout) is data the caller can show rather than an exception.</summary>
    public sealed record AiNameSuggestionsResult(
        [property: JsonProperty("suggestions")] IReadOnlyList<AiNameSuggestion> Suggestions,
        [property: JsonProperty("error")] string? Error);

    /// <summary>Single-name answer (the rename dialog's ghost text).</summary>
    public sealed record AiNameSuggestionResult(
        [property: JsonProperty("name")] string? Name,
        [property: JsonProperty("error")] string? Error);

    /// <summary>One abbreviation the model proposed for a naming template.</summary>
    public sealed record AiAbbreviationSuggestion(
        [property: JsonProperty("full")] string Full,
        [property: JsonProperty("abbr")] string Abbr);

    /// <summary>A naming template inferred from an example, with the abbreviations it relies on.</summary>
    public sealed record AiTemplateSuggestionResult(
        [property: JsonProperty("template")] string? Template,
        [property: JsonProperty("abbreviations")] IReadOnlyList<AiAbbreviationSuggestion> Abbreviations,
        [property: JsonProperty("error")] string? Error);

    /// <summary>Which models a provider offers. <see cref="Running"/> false means the endpoint could not
    /// be reached at all — a different situation from "reachable but offering nothing".</summary>
    public sealed record AiModelsResult(
        [property: JsonProperty("running")] bool Running,
        [property: JsonProperty("models")] IReadOnlyList<string>? Models,
        [property: JsonProperty("error")] string? Error);
}
