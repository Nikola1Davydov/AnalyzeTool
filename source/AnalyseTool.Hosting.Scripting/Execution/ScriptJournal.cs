using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalyseTool.Hosting.Scripting.Execution;

/// <summary>
///     One run of a script, as the journal keeps it.
/// </summary>
/// <param name="Time">When the run ended.</param>
/// <param name="Revit">The Revit version; <see langword="null" /> when the code did not compile and Revit was not reached.</param>
/// <param name="Document">The title of the active document, if any.</param>
/// <param name="DryRun">Whether the changes were rolled back on purpose.</param>
/// <param name="Outcome"><c>succeeded</c> or an error code.</param>
/// <param name="Changes">What the script changed; for a failed run nothing stays.</param>
/// <param name="Error">The error message of a failed run.</param>
/// <param name="Code">The code as it was sent.</param>
internal sealed record ScriptJournalEntry(
    DateTimeOffset Time,
    string? Revit,
    string? Document,
    bool DryRun,
    string Outcome,
    ScriptChanges? Changes,
    string? Error,
    string Code);

/// <summary>
///     The journal of scripts: every run of <c>ExecuteCode</c>, one JSON line in <c>%LOCALAPPDATA%\&lt;add-in&gt;\scripts\&lt;date&gt;.jsonl</c>.
/// </summary>
/// <remarks>
///     Code that changed a model on the user's behalf must be traceable: what ran, when, on which document, with which
///     outcome. A failure to write is logged by the caller and never fails the run.
/// </remarks>
internal sealed class ScriptJournal(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Lock _gate = new();

    /// <summary>Gets the folder of the journal files.</summary>
    public string Directory { get; } = directory;

    public void Write(ScriptJournalEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, Options) + Environment.NewLine;
        var path = Path.Combine(Directory, $"{entry.Time.LocalDateTime:yyyy-MM-dd}.jsonl");

        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.AppendAllText(path, line);
        }
    }
}
