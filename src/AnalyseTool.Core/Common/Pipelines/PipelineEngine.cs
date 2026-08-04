using Newtonsoft.Json.Linq;
using Serilog;

namespace AnalyseTool.Core.Common.Pipelines
{
    internal enum NodeState { Queued, Executing, Completed, Failed, Skipped }

    /// <summary>Progress for one node, raised as the run moves. Carries the error text rather than the
    /// exception: this crosses to a UI and to a log, neither of which wants a stack trace.</summary>
    internal sealed record NodeProgress(string NodeId, string Command, NodeState State, string? Error = null);

    /// <summary>One node's outcome, in file order.</summary>
    internal sealed record NodeOutcome(string NodeId, string Command, NodeState State, object? Result, string? Error);

    internal enum RunState { Completed, Failed, Cancelled }

    /// <summary>
    /// What a run leaves behind. Doubles as the run receipt: an exported artifact embeds it so the
    /// question "what produced this?" has an answer that does not depend on anyone having taken notes.
    /// </summary>
    internal sealed record PipelineRunResult(
        string PipelineId,
        RunState State,
        IReadOnlyList<NodeOutcome> Nodes)
    {
        /// <summary>The node the run stopped on, or null when it ran to the end. A cancelled run reports
        /// this too: Stop ends the run but does not undo it, so which node was reached is exactly what
        /// tells the user how much of the model was already written.</summary>
        public string? StoppedAt => Nodes.FirstOrDefault(n => n.State is NodeState.Failed)?.NodeId;
    }

    /// <summary>How a node reaches the platform. One implementation today (the local
    /// <see cref="CommandQueueDispatcher"/>); the interface exists so the engine never learns where a
    /// command actually runs.</summary>
    internal interface INodeDispatcher
    {
        Task<object?> ExecuteAsync(string command, JToken payload, CancellationToken ct);
    }

    /// <summary>
    /// Executes a <see cref="PipelineDocument"/>. Linear, synchronous, one node at a time.
    ///
    /// <para><b>A run is not atomic and does not hold the Revit thread.</b> Work raised by other
    /// transports runs between nodes, so a mutating node may not assume the state an earlier node
    /// observed still holds — it re-checks its own preconditions, which is what the existing write modes
    /// (Overwrite / OnlyIfEmpty / SkipIfEqual) are for. Leasing the Revit thread for the length of a run
    /// would freeze the UI and edit the most delicate part of dispatch, and is not worth doing before a
    /// single pipeline has ever run.</para>
    /// </summary>
    internal sealed class PipelineEngine
    {
        private readonly INodeDispatcher _dispatcher;

        public PipelineEngine(INodeDispatcher dispatcher) => _dispatcher = dispatcher;

        public async Task<PipelineRunResult> RunAsync(
            PipelineDocument pipeline,
            IProgress<NodeProgress>? progress = null,
            CancellationToken ct = default)
        {
            List<NodeOutcome> outcomes = new();
            Dictionary<string, object?> results = new(StringComparer.Ordinal);
            RunState state = RunState.Completed;

            foreach (PipelineNode node in pipeline.Nodes)
            {
                progress?.Report(new NodeProgress(node.Id, node.Command, NodeState.Executing));

                try
                {
                    JToken payload = BuildPayload(node, results);
                    object? result = await _dispatcher.ExecuteAsync(node.Command, payload, ct).ConfigureAwait(false);

                    results[node.Id] = result;
                    outcomes.Add(new NodeOutcome(node.Id, node.Command, NodeState.Completed, result, null));
                    progress?.Report(new NodeProgress(node.Id, node.Command, NodeState.Completed));
                }
                // FIRST, and unconditionally: cancellation travels the same path as a real failure, so a
                // single catch(Exception) consulting OnFailure would let a node marked Continue swallow
                // the user's Stop. Cancellation is not a failure and cannot be disabled by a node.
                catch (OperationCanceledException)
                {
                    outcomes.Add(new NodeOutcome(node.Id, node.Command, NodeState.Skipped, null, "Cancelled."));
                    progress?.Report(new NodeProgress(node.Id, node.Command, NodeState.Skipped, "Cancelled."));
                    Log.Information("Pipeline {Pipeline} cancelled at node {Node}", pipeline.Id, node.Id);
                    return new PipelineRunResult(pipeline.Id, RunState.Cancelled, outcomes);
                }
                catch (Exception ex)
                {
                    outcomes.Add(new NodeOutcome(node.Id, node.Command, NodeState.Failed, null, ex.Message));
                    progress?.Report(new NodeProgress(node.Id, node.Command, NodeState.Failed, ex.Message));
                    Log.Warning(ex, "Pipeline {Pipeline}: node {Node} ({Command}) failed",
                        pipeline.Id, node.Id, node.Command);

                    if (node.OnFailure == NodeFailureAction.Stop)
                        return new PipelineRunResult(pipeline.Id, RunState.Failed, outcomes);

                    // Continue was asked for explicitly, so the run goes on — but it is not a success.
                    state = RunState.Failed;
                }
            }

            return new PipelineRunResult(pipeline.Id, state, outcomes);
        }

        /// <summary>Literal params, with bound values layered on top — a binding wins over a literal of the
        /// same name, since a literal there is a leftover from before the node was connected.</summary>
        private static JToken BuildPayload(PipelineNode node, IReadOnlyDictionary<string, object?> results)
        {
            JObject payload = node.Params is null ? new JObject() : (JObject)node.Params.DeepClone();
            if (node.Bind is null) return payload;

            foreach (KeyValuePair<string, string> binding in node.Bind)
                payload[binding.Key] = Resolve(binding.Value, results);

            return payload;
        }

        /// <summary>Resolves "&lt;nodeId&gt;" or "&lt;nodeId&gt;.&lt;path&gt;" against what has run so far.
        /// An unknown node or a missing field throws rather than binding null: a pipeline quietly passing
        /// null into a mutating command is the failure mode worth being loud about.</summary>
        private static JToken Resolve(string reference, IReadOnlyDictionary<string, object?> results)
        {
            int dot = reference.IndexOf('.');
            string nodeId = dot < 0 ? reference : reference[..dot];

            if (!results.TryGetValue(nodeId, out object? source))
                throw new InvalidOperationException(
                    $"Binding '{reference}' refers to node '{nodeId}', which has not produced a result. " +
                    "Nodes run in file order — a binding can only read a node listed before this one.");

            JToken token = source is null ? JValue.CreateNull() : JToken.FromObject(source);
            if (dot < 0) return token;

            string path = reference[(dot + 1)..];
            return token.SelectToken(path)
                   ?? throw new InvalidOperationException(
                       $"Binding '{reference}' found no '{path}' in the result of node '{nodeId}'.");
        }
    }
}
