using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AnalyseTool.Core.Common.Pipelines
{
    /// <summary>
    /// What happens to the RUN when a node's command throws. Deliberately two values, not three:
    /// "stop this node but carry on" is indistinguishable from "stop" in a linear pipeline, because the
    /// next node would have no input. It becomes meaningful only with branching, which V1 does not have,
    /// so the name is left unused rather than implemented as a synonym.
    /// </summary>
    internal enum NodeFailureAction
    {
        /// <summary>End the run. The default for every node — see <see cref="PipelineNode.OnFailure"/>.</summary>
        Stop,

        /// <summary>Record the failure and continue. An explicit opt-in for nodes whose failure genuinely
        /// should not invalidate the run — a trailing export, say.</summary>
        Continue,
    }

    /// <summary>One step: a registered command plus the payload to call it with.</summary>
    internal sealed class PipelineNode
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>Registered command name, exactly as the dispatcher knows it (extension commands are
        /// "&lt;id&gt;.&lt;name&gt;").</summary>
        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        /// <summary>Which version of the command's contract this node was authored against. Reserved from
        /// v1 on purpose: without it an old file fails silently against a newer command — it would still
        /// run, just do something subtly different — and retrofitting the field later cannot repair files
        /// already in circulation.</summary>
        [JsonProperty("contract")]
        public int Contract { get; set; } = 1;

        /// <summary>The literal part of the payload. Values taken from an upstream node come from
        /// <see cref="Bind"/> instead, and are layered on top of this.</summary>
        [JsonProperty("params")]
        public Newtonsoft.Json.Linq.JObject? Params { get; set; }

        /// <summary>
        /// Payload property → where to read it from, as "&lt;nodeId&gt;" for that node's whole result or
        /// "&lt;nodeId&gt;.&lt;path&gt;" for one field of it.
        /// <para>Explicit rather than merging an upstream result into the payload by name: commands take
        /// specific shapes, name collisions between unrelated nodes are silent, and the graph validator
        /// can check an explicit binding against the two declared schemas (#89) while it cannot check a
        /// convention.</para>
        /// </summary>
        [JsonProperty("bind")]
        public Dictionary<string, string>? Bind { get; set; }

        /// <summary>Applies ONLY when the command throws. A partial result — 488 of 500 written — is a
        /// result, not a failure, and belongs in what the command returns. Absent → <see cref="NodeFailureAction.Stop"/>,
        /// for every node: a default read from the command's Destructive flag would live in the catalogue
        /// rather than in the file, so one pipeline could behave differently on two installations.</summary>
        [JsonProperty("onFailure")]
        [JsonConverter(typeof(StringEnumConverter))]
        public NodeFailureAction OnFailure { get; set; } = NodeFailureAction.Stop;

        /// <summary>
        /// Keys the deserializer did not recognise, kept rather than dropped.
        ///
        /// <para>Silently discarding them is how a pipeline ends up doing nothing while looking correct:
        /// an author who writes the conditions as <c>node.where</c> instead of <c>node.params.where</c>
        /// gets a Filter with no conditions, and neither the run nor the validator can say why, because by
        /// the time either looks the key no longer exists. This is what lets the validator name it.</para>
        ///
        /// <para>Collected instead of refused so a file written against a later build still loads —
        /// unknown keys are a warning, and only <see cref="PipelineDocument.Schema"/> refuses.</para>
        /// </summary>
        [JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken>? Unknown { get; set; }
    }

    /// <summary>A connection. V1 executes nodes in file order and uses edges only for validation and for
    /// the editor to draw; branching is out of scope, so a node has at most one incoming edge.</summary>
    internal sealed class PipelineEdge
    {
        [JsonProperty("from")]
        public string From { get; set; } = string.Empty;

        [JsonProperty("to")]
        public string To { get; set; } = string.Empty;
    }

    /// <summary>
    /// A <c>.atpipe</c> file. The format is OURS and stays ours: if the engine is ever replaced by a
    /// third-party workflow runtime, that runtime interprets this, rather than this being reshaped
    /// around it.
    /// </summary>
    internal sealed class PipelineDocument
    {
        /// <summary>File schema version, not the pipeline's. Bumped when THIS shape changes.</summary>
        [JsonProperty("schema")]
        public int Schema { get; set; } = 1;

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("author")]
        public string? Author { get; set; }

        /// <summary>The pipeline's own version, for the author to move as they see fit.</summary>
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonProperty("nodes")]
        public List<PipelineNode> Nodes { get; set; } = new();

        [JsonProperty("edges")]
        public List<PipelineEdge> Edges { get; set; } = new();

        /// <summary>
        /// Run state, for a run suspended at an approval node and resumed later — possibly after a Revit
        /// restart. Nothing writes it in V1, which executes straight through; the field exists now because
        /// adding it once files are in circulation would be a migration, and reserving it costs a line.
        /// </summary>
        [JsonProperty("state")]
        public Newtonsoft.Json.Linq.JObject? State { get; set; }

        /// <summary>Unrecognised top-level keys — see <see cref="PipelineNode.Unknown"/>.</summary>
        [JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken>? Unknown { get; set; }
    }
}
