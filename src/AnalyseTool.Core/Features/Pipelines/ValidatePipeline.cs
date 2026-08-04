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
                      "Returns { ok, errors: [], warnings: [] }.",
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
                    ? PipelineStore.Parse(req.Pipeline.ToString(), "the inline pipeline")
                    : PipelineStore.Load(req.Name ?? string.Empty);
            }
            catch (Exception ex)
            {
                return Task.FromResult<object?>(
                    new PipelineValidationResult(false, new[] { ex.Message }, Array.Empty<string>()));
            }

            return Task.FromResult<object?>(Validate(doc));
        }

        internal static PipelineValidationResult Validate(PipelineDocument doc)
        {
            List<string> errors = new();
            List<string> warnings = new();

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

                ValidateBindings(node, seen, reg, errors, warnings);
            }

            foreach (PipelineEdge edge in doc.Edges)
            {
                if (!seen.Contains(edge.From)) errors.Add($"Edge from unknown node '{edge.From}'.");
                if (!seen.Contains(edge.To)) errors.Add($"Edge to unknown node '{edge.To}'.");
            }

            return new PipelineValidationResult(errors.Count == 0, errors, warnings);
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
            public JObject? Pipeline { get; set; }
        }
    }

    /// <summary>Errors block a run; warnings do not. The split matters because most of what validation
    /// can say about a payload is a suspicion, and refusing to run on a suspicion would make the check
    /// something authors switch off.</summary>
    internal sealed record PipelineValidationResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("errors")] IReadOnlyList<string> Errors,
        [property: JsonProperty("warnings")] IReadOnlyList<string> Warnings);
}
