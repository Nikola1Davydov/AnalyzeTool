using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Serilog;

namespace AnalyseTool.Core.Common.Pipelines
{
    // The enums serialize as STRINGS: these cross to a UI, to a log and to an agent, and "2" tells none
    // of them anything. Property names are spelled out for the same reason they are everywhere else —
    // Newtonsoft writes declared names, while a published schema is generated in camelCase.

    [JsonConverter(typeof(StringEnumConverter))]
    internal enum NodeState { Queued, Executing, Completed, Failed, Skipped }

    /// <summary>Progress for one node, raised as the run moves. Carries the error text rather than the
    /// exception: this crosses to a UI and to a log, neither of which wants a stack trace.</summary>
    internal sealed record NodeProgress(string NodeId, string Command, NodeState State, string? Error = null);

    /// <summary>One node's outcome, in file order.</summary>
    internal sealed record NodeOutcome(
        [property: JsonProperty("nodeId")] string NodeId,
        [property: JsonProperty("command")] string Command,
        [property: JsonProperty("state")] NodeState State,
        [property: JsonProperty("result")] object? Result,
        [property: JsonProperty("error")] string? Error);

    [JsonConverter(typeof(StringEnumConverter))]
    internal enum RunState { Completed, Failed, Cancelled }

    /// <summary>
    /// What a run leaves behind. Doubles as the run receipt: an exported artifact embeds it so the
    /// question "what produced this?" has an answer that does not depend on anyone having taken notes.
    /// </summary>
    internal sealed record PipelineRunResult(
        [property: JsonProperty("pipelineId")] string PipelineId,
        [property: JsonProperty("state")] RunState State,
        [property: JsonProperty("nodes")] IReadOnlyList<NodeOutcome> Nodes)
    {
        /// <summary>The node the run stopped on, or null when it ran to the end. Worth its own field
        /// rather than leaving the caller to scan the list: Stop ends a run but does not undo it, so how
        /// far it got is exactly what says how much of the model was already written.</summary>
        [JsonProperty("stoppedAt")]
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

            // "items[]" normalised to "items[*]". An agent asked to write a binding produced the empty
            // brackets on its first attempt, and they cannot mean anything else — while raw JSONPath
            // answers them with "Array index expected.", which names neither the binding nor the node.
            string path = reference[(dot + 1)..].Replace("[]", "[*]");

            try
            {
                // A wildcard path asks for ALL the matches, not the first one. Without this a pipeline can
                // filter a list and then only ever act on item [0] — which is what the first real run of a
                // filter-then-isolate pipeline ran into. Matches that are themselves arrays are spliced, so
                // "items[*].failingElements" over rows holding id arrays yields ONE flat id list, which is
                // the shape the command downstream actually takes.
                if (path.Contains('*') || path.Contains("..") || path.Contains("[?"))
                {
                    JArray collected = new();
                    foreach (JToken match in token.SelectTokens(path))
                    {
                        if (match is JArray inner)
                            foreach (JToken value in inner) collected.Add(value);
                        else
                            collected.Add(match);
                    }

                    // Nothing collected has two entirely different causes, and treating them alike was
                    // wrong in both directions: a path naming a field that does not exist has to fail,
                    // while a filter that kept no rows has simply found nothing to do. The second is the
                    // ordinary outcome of a purge pipeline run twice, and it was being reported as a
                    // broken pipeline. Diagnose() tells them apart by walking the path.
                    return collected.Count > 0 ? collected : Diagnose(token, path, reference, nodeId);
                }

                return token.SelectToken(path)
                       ?? throw new InvalidOperationException(
                           $"Binding '{reference}' found no '{path}' in the result of node '{nodeId}'.");
            }
            catch (JsonException ex)
            {
                // A malformed path otherwise surfaces as a bare parser message with no idea which of a
                // dozen bindings produced it. Whoever wrote the pipeline — increasingly an AI correcting
                // its own file — needs the node and the expression, not the parser's opinion.
                throw new InvalidOperationException(
                    $"Binding '{reference}' on node reading '{nodeId}' is not a valid path: {ex.Message} " +
                    "Use \"node.field\", or \"node.items[*].field\" to collect every match.", ex);
            }
        }

        /// <summary>
        /// Why a wildcard collected nothing: an empty list, or a shape that is not there.
        ///
        /// Both used to fail, which made "purge unused families" report a broken pipeline on the second
        /// run — the first run had left nothing unused, the filter kept no rows, and finding nothing to
        /// purge is the correct answer, not an error. Failing them alike also cost the diagnosis in the
        /// other direction: "matched nothing" never said WHICH part of the path was missing.
        ///
        /// So the path is walked one step at a time. A wildcard over a list that exists and is empty is
        /// an empty result; a name that is not in what precedes it is a wrong shape, and the message
        /// names the step and the keys that ARE there.
        /// </summary>
        private static JArray Diagnose(JToken token, string path, string reference, string nodeId)
        {
            // "items[*].id" walked as items → [*] → id, so the array and the wildcard over it are two
            // separate questions. Recursive descent does not decompose this way, so it keeps the blunt
            // answer rather than a guess at which of the levels it searched was the intended one.
            if (path.Contains(".."))
                throw new InvalidOperationException(
                    $"Binding '{reference}' matched nothing in the result of node '{nodeId}'.");

            List<string> steps = new();
            foreach (string segment in path.Split('.'))
            {
                int bracket = segment.IndexOf('[');
                if (bracket < 0) { steps.Add(segment); continue; }
                if (bracket > 0) steps.Add(segment[..bracket]);
                steps.Add(segment[bracket..]);
            }

            string prefix = string.Empty;
            List<JToken> current = new() { token };

            foreach (string step in steps)
            {
                string next = step.StartsWith('[')
                    ? prefix + step
                    : prefix.Length == 0 ? step : $"{prefix}.{step}";

                List<JToken> matches = token.SelectTokens(next).ToList();
                if (matches.Count > 0)
                {
                    prefix = next;
                    current = matches;
                    continue;
                }

                // A wildcard over lists that are all genuinely empty: nothing to do, not a wrong shape.
                if (step.StartsWith('[') && current.All(t => t is JArray { Count: 0 }))
                    return new JArray();

                throw new InvalidOperationException(
                    $"Binding '{reference}' found no '{step}' in the result of node '{nodeId}'" +
                    Available(current) +
                    " A binding names a field of what the node returns; check the node's declared output.");
            }

            // Every step matched, yet nothing was collected — the matches were themselves empty arrays
            // and splicing them left nothing. Empty, for the same reason as above.
            return new JArray();
        }

        /// <summary>The keys actually present, so a wrong field name is corrected from the message.</summary>
        private static string Available(IReadOnlyList<JToken> tokens)
        {
            string[] keys = tokens
                .Select(t => t is JArray { Count: > 0 } array ? array[0] : t)
                .OfType<JObject>()
                .SelectMany(o => o.Properties().Select(p => p.Name))
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .ToArray();

            return keys.Length == 0 ? "." : $"; what is there: {string.Join(", ", keys)}.";
        }
    }
}
