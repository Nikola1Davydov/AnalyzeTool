using AnalyseTool.Tools.Shared;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Families
{
    // Result types for the Families commands that used to return anonymous objects. Named so they can be
    // declared through RevitCommandAttribute.OutputType: a caller (an MCP agent, a pipeline validator)
    // then reads what comes back instead of inferring it from the description.
    //
    // Every property spells its camelCase JSON name out. The wire is written by Newtonsoft, which uses
    // declared names by default, while the published schema is generated with Web defaults (camelCase) —
    // leave the attribute off and the schema quietly misnames what it describes.

    /// <summary>A rendered family thumbnail. <see cref="DataUri"/> is null when none could be produced.</summary>
    public sealed record FamilyPreviewResult(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("dataUri")] string? DataUri);

    /// <summary>A rendered thumbnail for an on-disk library file.</summary>
    public sealed record LibraryPreviewResult(
        [property: JsonProperty("path")] string Path,
        [property: JsonProperty("dataUri")] string? DataUri);

    /// <summary>The library listing. <see cref="Count"/> is redundant with the list and kept because the
    /// palette reads it directly.</summary>
    public sealed record LibraryFamiliesResult(
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("families")] IReadOnlyList<LibraryFamily> Families);

    /// <summary>Outcome of loading a batch of .rfa files.</summary>
    public sealed record LoadFamiliesResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("loaded")] int Loaded,
        [property: JsonProperty("failed")] int Failed,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning> Warnings);

    /// <summary>Outcome of starting interactive placement. Interactive: it returns once the user has
    /// finished or cancelled, so it means nothing to an unattended caller.</summary>
    public sealed record PlaceInstanceResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning> Warnings);

    /// <summary>Outcome of a purge, shared by the family and the type variants — same counts, same
    /// meaning, and a caller that handles one handles the other.</summary>
    public sealed record PurgeResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("deleted")] int Deleted,
        [property: JsonProperty("failed")] int Failed,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning> Warnings);
}
