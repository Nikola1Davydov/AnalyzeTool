using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Pipelines;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>
    /// Runs a pipeline: nodes in file order, each through the platform's own entry point.
    ///
    /// <para>Validated first, and refused on any error. A mutating pipeline that dies on node 6 because
    /// node 7 names a command this installation lacks has already changed the model for nothing, and
    /// everything of that kind is knowable before the first transaction.</para>
    ///
    /// <para><b>Stop ends a run; it does not undo one.</b> Nodes that committed stay committed — a run is
    /// not a transaction — so the result reports where it got to.</para>
    /// </summary>
    [RevitCommand(
        Description = "Runs a saved or inline pipeline, node by node, and returns what each one did. " +
                      "Payload: { name } for a saved pipeline, or { pipeline } inline. Validates first and " +
                      "refuses to start on any error. Stop ends the run but does NOT roll back the nodes " +
                      "that already committed.",
        Destructive = true, // a pipeline is as destructive as the commands in it
        InputType = typeof(RunPipeline.Request),
        OutputType = typeof(PipelineRunResult),
        // Not exposed to an agent YET, and not an oversight. The MCP bridge decides per COMMAND what an
        // agent may invoke; nodes dispatch under this command's own identity, so an agent able to call
        // RunPipeline with an inline document could reach commands it is refused directly. Exposing it
        // needs the run to carry the caller's policy into every node (CommandRequest.Gate is the hook) —
        // a deliberate piece of work, not a flag flip.
        HiddenFromMcp = true)]
    internal sealed class RunPipeline : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            PipelineDocument doc = req.Pipeline is not null
                ? PipelineStore.ParseInline(req.Pipeline, "the inline pipeline")
                : PipelineStore.Load(req.Name ?? string.Empty);

            PipelineValidationResult validation = ValidatePipeline.Validate(doc);
            if (!validation.Ok)
                throw new InvalidOperationException(
                    "This pipeline cannot run: " + string.Join(" ", validation.Errors));

            foreach (string warning in validation.Warnings)
                Log.Warning("Pipeline {Pipeline}: {Warning}", doc.Id, warning);

            Log.Information("Pipeline {Pipeline} ({Name}): starting {Count} node(s)",
                doc.Id, doc.Name, doc.Nodes.Count);

            // Node progress is reported as run progress, so the caller's existing progress bar shows which
            // step is running without knowing anything about pipelines.
            //
            // The FRACTION is the contract, not the message text. `done` counts finished nodes, so
            // fraction × total is exactly the index of the node now running — which is how a UI
            // highlights it without parsing a sentence. The message stays human wording for the busy bar,
            // and nothing should read a node id back out of it.
            int total = Math.Max(doc.Nodes.Count, 1);
            int done = 0;
            Progress<NodeProgress> nodeProgress = new(p =>
            {
                if (p.State is NodeState.Completed or NodeState.Failed or NodeState.Skipped) done++;
                Progress?.Report(new ProgressInfo(done / (double)total, $"{p.NodeId}: {p.Command}"));
            });

            PipelineEngine engine = new(new CommandQueueDispatcher(CoreServices.Queue, Progress));
            PipelineRunResult result = await engine.RunAsync(doc, nodeProgress, ct);

            Log.Information("Pipeline {Pipeline} finished: {State}, stopped at {StoppedAt}",
                doc.Id, result.State, result.StoppedAt ?? "(ran to the end)");

            return result;
        }

        internal sealed class Request
        {
            [Description("Name of a saved pipeline (extension optional), or an absolute path.")]
            public string? Name { get; set; }

            [Description("The pipeline document inline, instead of a saved one.")]
            public object? Pipeline { get; set; }
        }
    }

    /// <summary>Lists the saved pipelines, so a caller can offer them without knowing the folder.</summary>
    [RevitCommand(
        Description = "Lists the names of the saved pipelines (.atpipe files in the AnalyseTool profile " +
                      "folder). Returns { pipelines: [name] }.",
        ReadOnly = true,
        OutputType = typeof(PipelineListResult),
        HiddenFromMcp = true)]
    internal sealed class ListPipelines : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(new PipelineListResult(PipelineStore.ListNames()));
    }

    internal sealed record PipelineListResult(
        [property: Newtonsoft.Json.JsonProperty("pipelines")] IReadOnlyList<string> Pipelines);
}
