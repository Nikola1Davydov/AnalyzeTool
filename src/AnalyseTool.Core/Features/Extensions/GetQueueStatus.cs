using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Read-only introspection of the platform's busy state. Deliberately NEVER touches the Revit
    /// thread (no RunInRevitAsync) — it must answer even while Revit is blocked, because that is
    /// exactly when the user (or an AI agent) asks "why is nothing happening?".
    /// </summary>
    [RevitCommand(
        Description = "Returns what AnalyseTool is doing right now: { running: [{ command, source, " +
                      "seconds }], pendingRevitWork, waitingSeconds, waitingForUser }. waitingForUser=true " +
                      "means Revit cannot execute queued work — the user is in a modal dialog or an edit " +
                      "mode. AI agents: check this before starting heavy commands and wait while busy. " +
                      "Read-only, answers instantly even when Revit is blocked.",
        ReadOnly = true)]
    internal sealed class GetQueueStatus : IRevitTask
    {
        /// <summary>Fallback: pending Revit work stalled longer than this (seconds) also counts as
        /// blocked — belt and braces next to the primary Idling-stamp signal.</summary>
        private const double StalledWorkThresholdSeconds = 4;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(Snapshot());

        /// <summary>Shared with the host's QueueChanged broadcast so the event payload and the poll
        /// answer are always the same shape.</summary>
        internal static object Snapshot()
        {
            DateTime now = DateTime.UtcNow;
            var running = CoreServices.Queue.Running
                .Select(r => new
                {
                    command = r.Command,
                    source = r.Source,
                    seconds = Math.Round((now - r.StartedUtc).TotalSeconds, 1),
                })
                .ToList();

            (int pending, double waitingSeconds, bool executing) = RevitTaskHub.Current?.Status ?? (0, 0, false);

            // Primary signal (the RevitDBExplorer technique): Revit hasn't idled for a while and it's
            // not OUR work item running → a modal dialog / edit mode / native command holds Revit.
            // Detected proactively, before anything is even enqueued. The stalled-queue check stays
            // as a fallback for states where Idling behaves unexpectedly.
            bool blocked = !executing &&
                (RevitAvailability.IsRevitBusy ||
                 (pending > 0 && waitingSeconds > StalledWorkThresholdSeconds));

            return new
            {
                running,
                pendingRevitWork = pending,
                waitingSeconds = Math.Round(waitingSeconds, 1),
                waitingForUser = blocked,
            };
        }
    }
}
