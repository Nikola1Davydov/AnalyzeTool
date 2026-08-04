using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Pipelines;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>
    /// Writes a pipeline into the pipelines folder, so something other than a text editor can author one.
    ///
    /// <para>This is the half of the story that was missing. The plan says pipelines get authored by an
    /// agent — "it took five steps in chat, save that as a pipeline" — and consumed as a button. Until
    /// something could WRITE a <c>.atpipe</c>, that could not happen, which also meant the question the
    /// whole editor decision hangs on ("are agent-authored pipelines actually circulating?") could never
    /// be answered either way.</para>
    ///
    /// <para><b>Exposed to MCP, unlike <c>RunPipeline</c>, and the asymmetry is the design.</b> Saving
    /// executes nothing: it leaves a file a human can read, and running it is a separate act they take
    /// deliberately. So an agent may propose a pipeline, and only a person starts one — which is the same
    /// division the approval gate makes inside a run.</para>
    /// </summary>
    [RevitCommand(
        Description = "Saves a pipeline as a .atpipe file in the AnalyseTool pipelines folder, so it can " +
                      "be reviewed and run later. Payload: { name, pipeline }. Validates and REPORTS " +
                      "problems, but still saves — a work-in-progress pipeline is allowed. Saving does not " +
                      "run anything. Returns { ok, name, path, replaced, validation }.",
        InputType = typeof(SavePipeline.Request),
        OutputType = typeof(SavePipelineResult))]
    internal sealed class SavePipeline : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (req.Pipeline is null)
                throw new InvalidOperationException("No pipeline to save.");

            string name = SafeName(req.Name);

            // Parsed before it is written: a file that cannot be read back is not a work in progress, it
            // is litter that fails later and further from the cause.
            PipelineDocument doc = PipelineStore.Parse(req.Pipeline.ToString(), "the pipeline being saved");

            // Validation problems do NOT block the save. An author (human or agent) building a pipeline
            // step by step passes through invalid states by definition, and a save that refuses them is a
            // save nobody uses. They are reported instead, right where they can still be acted on.
            PipelineValidationResult validation = ValidatePipeline.Validate(doc);

            Directory.CreateDirectory(PathProvider.PipelinesRoot);
            string path = Path.Combine(PathProvider.PipelinesRoot, name + PipelineStore.Extension);
            bool replaced = File.Exists(path);

            File.WriteAllText(path, JsonConvert.SerializeObject(doc, Formatting.Indented));

            Log.Information("Pipeline '{Name}' saved to {Path} ({Action}), {Errors} error(s), {Warnings} warning(s)",
                name, path, replaced ? "replaced" : "created", validation.Errors.Count, validation.Warnings.Count);

            return Task.FromResult<object?>(new SavePipelineResult(true, name, path, replaced, validation));
        }

        /// <summary>
        /// Reduces the requested name to a bare file name. Deliberately a whitelist: this command is
        /// reachable by an AI agent, and a name is the only part of the payload that becomes a path — so
        /// "../../autostart" must be impossible rather than merely unlikely. Everything outside the
        /// allowed set is dropped, and what remains can only ever name a file inside the pipelines folder.
        /// </summary>
        private static string SafeName(string? requested)
        {
            string cleaned = new((requested ?? string.Empty)
                .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-')
                .ToArray());

            cleaned = cleaned.Trim();
            if (cleaned.Length > 64) cleaned = cleaned[..64].Trim();

            if (cleaned.Length == 0)
                throw new InvalidOperationException(
                    "A pipeline name must contain letters, digits, spaces, underscores or hyphens.");

            return cleaned;
        }

        internal sealed class Request
        {
            [Description("File name to save under, without the extension. Letters, digits, spaces, _ and - only.")]
            public string? Name { get; set; }

            [Description("The pipeline document.")]
            public JObject? Pipeline { get; set; }
        }
    }

    internal sealed record SavePipelineResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("path")] string Path,
        [property: JsonProperty("replaced")] bool Replaced,
        [property: JsonProperty("validation")] PipelineValidationResult Validation);
}
