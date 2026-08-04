using AnalyseTool.Tools.Shared;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Actions
{
    // Result types for the Actions commands. These are the write commands a pipeline ends with, so what
    // they return is not a detail: an unattended caller has no other way to learn what actually landed.
    //
    // Both records carry error AND warnings, and they are different things. An error means the command
    // did not do its job; a warning means Revit objected to something and the command resolved it
    // without a dialog (see CollectingFailuresPreprocessor). A run can succeed with fifty warnings.
    //
    // camelCase spelled out: the wire is written by Newtonsoft (declared names by default) and the
    // schema published for OutputType is generated with Web defaults (camelCase).

    /// <summary>Outcome of a bulk parameter write. <see cref="Skipped"/> counts items whose element or
    /// parameter was not found, was read-only, or that the write mode filtered out — the difference
    /// between "500 written" and "460 written, 40 quietly dropped".</summary>
    public sealed record SetDataResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("written")] int Written,
        [property: JsonProperty("skipped")] int Skipped,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning> Warnings);

    /// <summary>Outcome of a temporary isolate in the active view.</summary>
    public sealed record IsolationResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("isolated")] int Isolated,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning> Warnings);
}
