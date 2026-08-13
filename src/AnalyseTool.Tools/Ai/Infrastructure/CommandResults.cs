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

    /// <summary>Free-text answer from an AI analysis run — prose for a human to read, not data to act on.
    /// Wrapped in an object rather than returned as a bare string because a schema describes an object:
    /// a top-level string leaves nowhere to put <see cref="Error"/>, and MCP's structuredContent cannot
    /// carry it at all.</summary>
    public sealed record AiAnalysisResult(
        [property: JsonProperty("analysis")] string? Analysis,
        [property: JsonProperty("error")] string? Error);

    /// <summary>One parameter change the model proposes. Keyed by <see cref="ElementId"/> — the caller's
    /// own id, echoed back — so a row can be matched to what the model said about it.
    ///
    /// A wire type of its own rather than AiAnalysisService.ParameterAiEdit: that record is the shape the
    /// PROMPT asks the model for (System.Text.Json, PascalCase keys by instruction), and rewording a
    /// prompt should not silently rename a published schema's fields.</summary>
    public sealed record AiParameterEdit(
        [property: JsonProperty("elementId")] long ElementId,
        [property: JsonProperty("parameter")] string Parameter,
        [property: JsonProperty("oldValue")] string OldValue,
        [property: JsonProperty("newValue")] string NewValue,
        [property: JsonProperty("reason")] string Reason);

    /// <summary>Proposed parameter edits — a proposal, never an applied change: the caller decides, then
    /// writes them with SetDataToParameters. <see cref="Raw"/> is the model's unparsed answer, kept
    /// because when parsing yields nothing it is the only evidence of why.</summary>
    public sealed record AiEditsResult(
        [property: JsonProperty("edits")] IReadOnlyList<AiParameterEdit> Edits,
        [property: JsonProperty("raw")] string? Raw,
        [property: JsonProperty("error")] string? Error);

    /// <summary>One configured AI provider as the frontend may see it. There is no key field and never
    /// will be: <see cref="HasKey"/> says whether one is stored, and the key itself stays host-side,
    /// DPAPI-encrypted (see AiProviderRegistry).</summary>
    public sealed record AiProviderInfo(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("displayName")] string DisplayName,
        [property: JsonProperty("type")] string Type,
        [property: JsonProperty("baseUrl")] string BaseUrl,
        [property: JsonProperty("hasKey")] bool HasKey,
        [property: JsonProperty("timeoutSeconds")] int TimeoutSeconds,
        [property: JsonProperty("builtIn")] bool BuiltIn);

    /// <summary>The provider registry after a read, a save or a delete. All three commands answer the same
    /// question — what the registry looks like NOW — so they share one shape, and a caller that just
    /// changed something never has to ask again to find out what it changed.</summary>
    public sealed record AiProvidersResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("providers")] IReadOnlyList<AiProviderInfo> Providers,
        [property: JsonProperty("error")] string? Error);
}
