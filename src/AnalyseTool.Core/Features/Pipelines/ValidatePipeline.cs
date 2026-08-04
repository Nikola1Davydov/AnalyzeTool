using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Pipelines;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>
    /// Checks a pipeline WITHOUT running it. The point is that everything findable before the first
    /// transaction is found before it: a mutating pipeline that dies on node 6 because node 7 names a
    /// command this installation does not have has already changed the model for nothing.
    /// </summary>
    [RevitCommand(
        Description = "Validates a pipeline without running it: unknown commands, duplicate or dangling " +
                      "node ids, bindings that cannot resolve, and payload properties the target command " +
                      "does not declare. Payload: { name } for a saved pipeline, or { pipeline } inline. " +
                      "Returns { ok, errors: [], warnings: [], destructiveNodes: [] } — the last being " +
                      "the nodes that CHANGE the model, so a caller can confirm before running.",
        ReadOnly = true,
        InputType = typeof(ValidatePipeline.Request),
        OutputType = typeof(PipelineValidationResult))]
    internal sealed class ValidatePipeline : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            PipelineDocument doc;
            try
            {
                doc = req.Pipeline is not null
                    ? PipelineStore.ParseInline(req.Pipeline, "the inline pipeline")
                    : PipelineStore.Load(req.Name ?? string.Empty);
            }
            catch (Exception ex)
            {
                return Task.FromResult<object?>(
                    new PipelineValidationResult(
                        false, new[] { ex.Message }, Array.Empty<string>(), Array.Empty<string>()));
            }

            return Task.FromResult<object?>(Validate(doc));
        }

        internal static PipelineValidationResult Validate(PipelineDocument doc)
        {
            List<string> errors = new();
            List<string> warnings = new();
            List<string> destructive = new();

            // The id keys the run receipt and names the run in every log line. An empty one is not fatal,
            // but "pipeline '' finished" helps nobody afterwards.
            if (string.IsNullOrWhiteSpace(doc.Id))
                warnings.Add("The pipeline has no id; runs and exported artifacts will not be traceable to it.");

            foreach (string key in doc.Unknown?.Keys ?? Enumerable.Empty<string>())
                warnings.Add($"The pipeline has an unrecognised top-level key '{key}', which is ignored.");

            Dictionary<string, PipelineNode> byId = doc.Nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .GroupBy(n => n.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (PipelineNode node in doc.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id)) { errors.Add("A node has no id."); continue; }
                if (!seen.Add(node.Id)) errors.Add($"Duplicate node id '{node.Id}'.");

                CommandRegistration? reg = CoreServices.Queue.RegisteredCommands
                    .FirstOrDefault(c => string.Equals(c.Name, node.Command, StringComparison.OrdinalIgnoreCase));

                if (reg is null)
                {
                    // Named separately from a typo because the fix differs: install the extension that
                    // provides it, or edit the pipeline.
                    errors.Add($"Node '{node.Id}' calls '{node.Command}', which is not registered on this installation.");
                    continue;
                }

                // Collected here rather than derived by the caller: the flag lives on the registration,
                // which is a platform detail, and a UI asking "are you sure?" must not have to fetch the
                // whole command catalogue to find out what a run is about to change.
                if (reg.Destructive) destructive.Add($"{node.Id} ({node.Command})");

                ValidateNodeShape(node, reg, warnings);
                ValidateBindings(node, seen, reg, errors, warnings);
                ValidateUnfilteredDestructiveInput(node, byId, reg, errors, warnings);
            }

            foreach (PipelineEdge edge in doc.Edges)
            {
                if (!seen.Contains(edge.From)) errors.Add($"Edge from unknown node '{edge.From}'.");
                if (!seen.Contains(edge.To)) errors.Add($"Edge to unknown node '{edge.To}'.");
            }

            return new PipelineValidationResult(errors.Count == 0, errors, warnings, destructive);
        }

        /// <summary>
        /// Checks what the node CARRIES, as opposed to what it is wired to.
        ///
        /// <para>Both halves come from the same real failure. An agent wrote its Filter conditions as a
        /// key on the node instead of inside <c>params</c>; the deserializer dropped it, the Filter passed
        /// its whole input through, and the only thing standing between that and a purge of 187 family
        /// types was an unrelated typo in a binding. Nothing anywhere could say what had gone wrong,
        /// because the mistake had already been thrown away.</para>
        ///
        /// <para>Warnings rather than errors: a key we do not recognise may be an annotation from an
        /// editor we have not written yet, and a param we cannot vouch for may simply belong to a command
        /// that declares no input type. Refusing to run on either would make the check something authors
        /// switch off. The pair still turns "it silently did nothing" into one line naming the key.</para>
        /// </summary>
        private static void ValidateNodeShape(PipelineNode node, CommandRegistration reg, List<string> warnings)
        {
            HashSet<string> declared = InputProperties(reg);

            foreach (string key in node.Unknown?.Keys ?? Enumerable.Empty<string>())
            {
                // The likely case is worth its own sentence: the key IS a payload property, one level too
                // high. That is a one-line fix an author can act on without reading the format docs.
                string hint = declared.Contains(key)
                    ? $" '{node.Command}' takes '{key}' as a payload property — move it into \"params\"."
                    : string.Empty;

                warnings.Add($"Node '{node.Id}' has an unrecognised key '{key}', which is ignored.{hint}");
            }

            if (node.Params is null || declared.Count == 0) return;

            foreach (JProperty param in node.Params.Properties())
                if (!declared.Contains(param.Name))
                    warnings.Add($"Node '{node.Id}' passes '{param.Name}', which '{node.Command}' does not " +
                                 "declare. It will be sent and ignored.");
        }

        private static void ValidateBindings(
            PipelineNode node, HashSet<string> earlierNodeIds, CommandRegistration reg,
            List<string> errors, List<string> warnings)
        {
            if (node.Bind is null) return;

            HashSet<string> declared = InputProperties(reg);

            foreach (KeyValuePair<string, string> binding in node.Bind)
            {
                string sourceId = binding.Value.Split('.')[0];

                // Order matters, not mere existence: nodes run in file order, so a binding may only read
                // a node listed BEFORE this one. `seen` holds exactly those, this node included — which is
                // why a self-reference is caught here too.
                if (!earlierNodeIds.Contains(sourceId) || sourceId == node.Id)
                    errors.Add($"Node '{node.Id}' binds '{binding.Key}' to '{binding.Value}', " +
                               $"but '{sourceId}' does not run before it.");

                // A warning, not an error: the schema is empty for a command that declares no InputType,
                // and it degrades to a permissive object for an oversized one — neither means the property
                // is wrong, only that we cannot vouch for it.
                if (declared.Count > 0 && !declared.Contains(binding.Key))
                    warnings.Add($"Node '{node.Id}' binds '{binding.Key}', which '{node.Command}' does not declare. " +
                                 "It will be sent and probably ignored.");
            }
        }

        /// <summary>
        /// Refuses a destructive node fed by a Filter that filters nothing.
        ///
        /// <para>A Filter with no conditions passes its whole input through, deliberately, so a
        /// half-built pipeline still runs end to end. That is right while everything downstream only
        /// reads — and wrong the moment a purge or a delete is next, because "keep everything" then means
        /// "destroy everything". The first pipeline an AI wrote unprompted was exactly this shape: read
        /// 187 family types, filter with no conditions, purge — and it only spared the model because a
        /// malformed binding failed first.</para>
        ///
        /// <para>An error rather than a warning, since warnings do not stop a run and this one has to.
        /// The fix an author needs is one line: give the Filter its condition.</para>
        /// </summary>
        private static void ValidateUnfilteredDestructiveInput(
            PipelineNode node, IReadOnlyDictionary<string, PipelineNode> byId, CommandRegistration reg,
            List<string> errors, List<string> warnings)
        {
            if (node.Bind is null) return;

            foreach (KeyValuePair<string, string> binding in node.Bind)
            {
                string sourceId = binding.Value.Split('.')[0];
                if (!byId.TryGetValue(sourceId, out PipelineNode? source)) continue;
                if (!string.Equals(source.Command, "Filter", StringComparison.OrdinalIgnoreCase)) continue;

                // Case-insensitively, because the deserializer that feeds the RUNNING node is: Newtonsoft
                // matches "Where" to `Where` without comment. Reading this key more strictly than the
                // command does would refuse a pipeline that actually works.
                if (source.Params?.GetValue("where", StringComparison.OrdinalIgnoreCase) is JArray { Count: > 0 })
                    continue;

                string message =
                    $"Node '{source.Id}' is a Filter with no conditions, so it passes its entire input " +
                    $"through to '{node.Id}' ({node.Command}). {Carries(source)}";

                if (reg.Destructive)
                    errors.Add(message + " That command CHANGES the model — as written, this pipeline " +
                               "would act on everything the previous node returned. Give the Filter a " +
                               "condition, or remove it and bind directly if acting on everything is " +
                               "really the intent.");
                else
                    warnings.Add(message + " Harmless here, but the Filter is doing nothing.");
            }
        }

        /// <summary>
        /// What a node actually carries, appended to any finding about it.
        ///
        /// <para>Diagnosing the first agent-authored pipeline took several rounds purely because "a Filter
        /// with no conditions" does not distinguish a node whose <c>params</c> are empty from one whose
        /// conditions are under a name nothing reads — and answering that meant going and finding the
        /// file. The message can say it instead.</para>
        /// </summary>
        private static string Carries(PipelineNode node)
        {
            List<string> keys = node.Params?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
            string carried = keys.Count > 0
                ? $"Its params carry: {string.Join(", ", keys)}."
                : "It has no params at all.";

            if (node.Unknown is { Count: > 0 })
                carried += $" Keys ignored on this node: {string.Join(", ", node.Unknown.Keys)}.";

            return carried;
        }

        /// <summary>Top-level property names of the command's declared input schema; empty when it declares
        /// none, or when the schema degraded to a free-form object.</summary>
        private static HashSet<string> InputProperties(CommandRegistration reg)
        {
            try
            {
                if (JObject.Parse(reg.InputSchemaJson)["properties"] is JObject properties)
                    return new HashSet<string>(properties.Properties().Select(p => p.Name), StringComparer.Ordinal);
            }
            catch { /* an unreadable schema vouches for nothing, which is what an empty set means */ }
            return new HashSet<string>(StringComparer.Ordinal);
        }

        internal sealed class Request
        {
            [Description("Name of a saved pipeline (extension optional), or an absolute path.")]
            public string? Name { get; set; }

            [Description("The pipeline document inline, instead of a saved one.")]
            public object? Pipeline { get; set; }
        }
    }

    /// <summary>Errors block a run; warnings do not. The split matters because most of what validation
    /// can say about a payload is a suspicion, and refusing to run on a suspicion would make the check
    /// something authors switch off.</summary>
    internal sealed record PipelineValidationResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("errors")] IReadOnlyList<string> Errors,
        [property: JsonProperty("warnings")] IReadOnlyList<string> Warnings,
        /// <summary>Nodes whose command is <c>Destructive</c>, as "id (Command)". Not a problem — the
        /// answer to "what is this run about to change?", which is what a confirmation has to show.
        /// Empty for a read-only pipeline, which is exactly when no confirmation is warranted.</summary>
        [property: JsonProperty("destructiveNodes")] IReadOnlyList<string> DestructiveNodes);
}
