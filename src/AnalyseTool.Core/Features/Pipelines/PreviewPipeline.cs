using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Pipelines;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>
    /// Runs the READ-ONLY prefix of a pipeline and returns what each node produced.
    ///
    /// <para>Authoring a pipeline blind is the problem this solves. A schema says a node returns
    /// <c>rows</c>; it does not say that <c>rows</c> is empty because the command needed ids nobody
    /// passed it, or that a field is called <c>typeId</c> and not <c>id</c>. Both cost real runs to find
    /// out, and one of them nearly purged a model. Actual data, in the editor, before anything is saved,
    /// is the shortest path to a binding that is right the first time.</para>
    ///
    /// <para><b>It refuses to run anything that is not <c>ReadOnly</c>.</b> Not "skips" — refuses, naming
    /// the node: a preview that quietly stopped early would look like a node returning nothing, which is
    /// exactly the confusion this exists to remove. Previewing up to the node before the first write is
    /// always available, and that is where all the interesting shapes are anyway.</para>
    /// </summary>
    [RevitCommand(
        Description = "Runs a pipeline's READ-ONLY prefix and returns each node's actual result, so " +
                      "bindings can be written against real data. Payload: { pipeline } inline or " +
                      "{ name }, plus { untilNode } to stop after that node (default: the whole " +
                      "pipeline). Refuses, naming the node, if any node in the prefix changes the model.",
        ReadOnly = true,
        InputType = typeof(PreviewPipeline.Request),
        OutputType = typeof(PipelineRunResult))]
    internal sealed class PreviewPipeline : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            PipelineDocument doc = req.Pipeline is not null
                ? PipelineStore.ParseInline(req.Pipeline, "the inline pipeline")
                : PipelineStore.Load(req.Name ?? string.Empty);

            List<PipelineNode> prefix = Prefix(doc, req.UntilNode);

            foreach (PipelineNode node in prefix)
            {
                CommandRegistration? reg = CoreServices.Queue.RegisteredCommands
                    .FirstOrDefault(c => string.Equals(c.Name, node.Command, StringComparison.OrdinalIgnoreCase));

                if (reg is null)
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' calls '{node.Command}', which is not registered on this installation.");

                // ReadOnly is the positive claim, and its absence is the refusal — a command that does not
                // declare itself read-only is not assumed to be one just because it is not marked
                // Destructive either.
                if (!reg.ReadOnly)
                    throw new InvalidOperationException(
                        $"Preview stops at node '{node.Id}': '{node.Command}' is not read-only, and a " +
                        "preview never writes to the model. Preview up to the node before it, or run the " +
                        "pipeline for real from the Pipelines pane.");
            }

            // A prefix is a pipeline. Running it through the same engine means the preview resolves
            // bindings exactly as the real run will, which is the only reason its data is worth trusting.
            PipelineDocument previewDoc = new()
            {
                Schema = doc.Schema,
                Id = doc.Id,
                Name = doc.Name,
                Version = doc.Version,
                Nodes = prefix,
                Edges = doc.Edges.Where(e =>
                    prefix.Any(n => n.Id == e.From) && prefix.Any(n => n.Id == e.To)).ToList(),
            };

            // Reported the same way a real run is, so the editor lights nodes up as they go. The
            // fraction is what a UI reads: finished nodes over total, which makes fraction × total the
            // index of the node currently running.
            int total = Math.Max(prefix.Count, 1);
            int done = 0;
            Progress<NodeProgress> nodeProgress = new(p =>
            {
                if (p.State is NodeState.Completed or NodeState.Failed or NodeState.Skipped) done++;
                Progress?.Report(new ProgressInfo(done / (double)total, $"{p.NodeId}: {p.Command}"));
            });

            PipelineEngine engine = new(new CommandQueueDispatcher(CoreServices.Queue, Progress));
            return await engine.RunAsync(previewDoc, nodeProgress, ct).ConfigureAwait(false);
        }

        /// <summary>Nodes up to and including <paramref name="untilNode"/>; the whole list when it is
        /// empty, and an error when it names a node this pipeline does not have — silently previewing
        /// everything instead would run more than the caller asked for.</summary>
        private static List<PipelineNode> Prefix(PipelineDocument doc, string? untilNode)
        {
            if (string.IsNullOrWhiteSpace(untilNode)) return doc.Nodes.ToList();

            int index = doc.Nodes.FindIndex(n => string.Equals(n.Id, untilNode, StringComparison.Ordinal));
            if (index < 0)
                throw new InvalidOperationException($"This pipeline has no node '{untilNode}'.");

            return doc.Nodes.Take(index + 1).ToList();
        }

        internal sealed class Request
        {
            [Description("The pipeline document inline. Use this while editing an unsaved draft.")]
            public object? Pipeline { get; set; }

            [Description("Name of a saved pipeline, instead of an inline one.")]
            public string? Name { get; set; }

            [Description("Stop after this node id. Empty previews every node.")]
            public string? UntilNode { get; set; }
        }
    }
}
