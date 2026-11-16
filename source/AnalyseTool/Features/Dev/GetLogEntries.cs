#if DEBUG
using AnalyseTool.Infrastructure.Diagnostics;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Features.Dev;

/// <summary>
///     Debug-only command of the «Dev» page and of AI clients: returns the log records written since the caller's last call.
///     The page polls it; <see cref="LogBuffer" /> drops the records of the polling itself.
/// </summary>
[McpTool(ReadOnly = true, Title = "Add-in log")]
[Command(Description =
    "Returns the add-in's log records after the given sequence number, oldest first, at most the last 1000 (Debug build only). " +
    "Pass afterSequence 0 first, then the lastSequence of the previous answer to get only what is new. " +
    "minLevel filters the records: Warning shows refused and failed commands, Information also every command call.")]
public sealed class GetLogEntries(LogBuffer buffer) : ICommand<GetLogEntriesRequest, LogPage>
{
    /// <inheritdoc />
    public Task<LogPage> ExecuteAsync(GetLogEntriesRequest request, CommandContext context)
    {
        var entries = buffer.GetAfter(request.AfterSequence);

        // From all records, not the filtered ones: the next call must not see a skipped record again
        var last = entries.Count > 0 ? entries[^1].Sequence : request.AfterSequence;
        var shown = request.MinLevel is LogLevel.Trace ? entries : entries.Where(entry => entry.Level >= request.MinLevel).ToList();
        return Task.FromResult(new LogPage(shown, last));
    }
}

/// <summary>The input of <see cref="GetLogEntries" />.</summary>
/// <param name="AfterSequence">The <see cref="LogEntry.Sequence" /> of the last record the caller has; <c>0</c> for all.</param>
/// <param name="MinLevel">The lowest level returned; all by default.</param>
public sealed record GetLogEntriesRequest(long AfterSequence, LogLevel MinLevel = LogLevel.Trace);

/// <summary>The result of <see cref="GetLogEntries" />.</summary>
/// <param name="Entries">The new records, oldest first; at most <see cref="LogBuffer.Capacity" />.</param>
/// <param name="LastSequence">What to pass as <see cref="GetLogEntriesRequest.AfterSequence" /> next time.</param>
public sealed record LogPage(IReadOnlyList<LogEntry> Entries, long LastSequence);
#endif
