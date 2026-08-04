using AnalyseTool.Core.Common.Dispatch;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Core.Common.Pipelines
{
    /// <summary>
    /// Runs a node by enqueuing it into the platform's one entry point, exactly as a transport does.
    ///
    /// <para>Deliberately thin — the engine is a CALLER, not a second execution path. Everything the
    /// funnel already provides comes along for free: the running-command list behind the busy indicator,
    /// <c>GetQueueStatus</c> introspection, and the pre-execution gate. Nothing in dispatch changes to
    /// accommodate pipelines, which is the whole reason the engine could be added without touching it.</para>
    ///
    /// <para>There is no routing decision here, and no capability flag behind one: a command that never
    /// calls <c>RunInRevitAsync</c> already runs entirely off the Revit thread, because that method is
    /// the only path to it. "Orchestrator" is therefore a description of a command, not a mode.</para>
    /// </summary>
    internal sealed class CommandQueueDispatcher : INodeDispatcher
    {
        /// <summary>Transport identity in the log and in <c>GetQueueStatus</c>, so a node in flight is
        /// distinguishable from a window or an agent doing the same thing.</summary>
        public const string SourceName = "pipeline";

        private readonly CommandQueue _queue;
        private readonly IProgress<Sdk.ProgressInfo>? _progress;

        public CommandQueueDispatcher(CommandQueue queue, IProgress<Sdk.ProgressInfo>? progress = null)
        {
            _queue = queue;
            _progress = progress;
        }

        public Task<object?> ExecuteAsync(string command, JToken payload, CancellationToken ct) =>
            _queue.ExecuteAsync(new CommandRequest(command, payload, SourceName)
            {
                CancellationToken = ct,
                // Passed through so a long node (purging thousands of families) still animates its own
                // progress rather than looking hung for a minute.
                Progress = _progress,
            });
    }
}
