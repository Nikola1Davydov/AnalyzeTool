using AnalyseTool.Tools.Shared;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Dwg
{
    // Result type for the one Dwg command that writes. The read commands answer with the wire types
    // themselves (DwgStructure, DwgEntities) — wrapping those in a second, identical shape would buy a
    // caller nothing and give the schema two names for one thing.
    //
    // camelCase spelled out, as everywhere in Tools: Newtonsoft writes declared names, the schema
    // published for OutputType is generated with Web defaults.

    /// <summary>
    /// Outcome of converting DWG geometry into native Revit curves.
    ///
    /// <see cref="Created"/> and <see cref="Skipped"/> do not add up to <see cref="Matched"/> when
    /// <see cref="Truncated"/> is set — the cap stopped the read before the rest were even looked at.
    /// <see cref="SkippedReasons"/> is what makes the skipped count answerable: "812 skipped" is not a
    /// result, "812: block references need a family mapping" is.
    /// </summary>
    public sealed record DwgImportResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("created")] int Created,
        [property: JsonProperty("skipped")] int Skipped,
        [property: JsonProperty("skippedReasons")] IReadOnlyDictionary<string, int> SkippedReasons,
        [property: JsonProperty("matched")] int Matched,
        [property: JsonProperty("truncated")] bool Truncated,
        [property: JsonProperty("unit")] string Unit,
        [property: JsonProperty("feetPerUnit")] double FeetPerUnit,
        [property: JsonProperty("recentered")] bool Recentered,
        [property: JsonProperty("offsetFeet")] IReadOnlyList<double> OffsetFeet,
        [property: JsonProperty("target")] string Target,
        [property: JsonProperty("viewName")] string? ViewName,
        [property: JsonProperty("lineStylesMapped")] int LineStylesMapped,
        [property: JsonProperty("unmappedLayers")] IReadOnlyList<string> UnmappedLayers,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning> Warnings);
}
