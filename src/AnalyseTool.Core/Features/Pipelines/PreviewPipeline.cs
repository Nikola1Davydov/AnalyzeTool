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
    /// exactly the confusion this exists to remove.</para>
    ///
    /// <para>With <c>untilNode</c> it runs that node's DEPENDENCY CLOSURE — the node plus everything it
    /// transitively binds from — rather than every node before it. The difference matters as soon as a
    /// pipeline writes: a read placed after a purge has nothing to do with it beyond running later, and
    /// a prefix-based preview would refuse for a reason that has nothing to do with the node asked
    /// about. What such a preview shows is the model AS IT IS NOW, not as it will be once the write in
    /// front of it has run — which is the honest answer to "what shape does this return".</para>
    /// </summary>
    [RevitCommand(
        Description = "Runs a pipeline's READ-ONLY prefix and returns each node's actual result, so " +
                      "bindings can be written against real data. Payload: { pipeline } inline or " +
                      "{ name }, plus { untilNode } to preview ONE node — it and everything it reads " +
                      "from, so a read that merely sits after a write can still be checked. Without " +
                      "untilNode the whole pipeline is previewed. Refuses, naming the node, if what it " +
                      "would have to run changes the model.",
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

            List<PipelineNode> prefix = Selection(doc, req.UntilNode);

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
                        "preview never writes to the model. Select a node that does not read from it — a " +
                        "preview runs only what the selected node depends on — or run the pipeline for " +
                        "real from the Pipelines pane.");
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

        /// <summary>
        /// What a preview has to run: the whole pipeline when no node is named, otherwise that node and
        /// everything it transitively reads from, in file order.
        ///
        /// <para>Only what it READS. Running the prefix instead would drag in every unrelated node that
        /// happens to sit earlier, and refuse the moment one of them writes — a read placed after a purge
        /// would become uncheckable for a reason that has nothing to do with it.</para>
        /// </summary>
        private static List<PipelineNode> Selection(PipelineDocument doc, string? untilNode)
        {
            if (string.IsNullOrWhiteSpace(untilNode)) return doc.Nodes.ToList();

            int index = doc.Nodes.FindIndex(n => string.Equals(n.Id, untilNode, StringComparison.Ordinal));
            if (index < 0)
                throw new InvalidOperationException($"This pipeline has no node '{untilNode}'.");

            HashSet<string> needed = new(StringComparer.Ordinal);
            Walk(doc.Nodes[index]);

            void Walk(PipelineNode node)
            {
                if (!needed.Add(node.Id)) return; // also what keeps a cycle in a hand-written file finite
                foreach (string reference in node.Bind?.Values ?? Enumerable.Empty<string>())
                {
                    string sourceId = reference.Split('.')[0];
                    PipelineNode? source = doc.Nodes.FirstOrDefault(n =>
                        string.Equals(n.Id, sourceId, StringComparison.Ordinal));
                    if (source is not null) Walk(source);
                }
            }

            // File order is preserved: the engine resolves a binding against what has already run, so
            // the sources have to come first exactly as they do in a real run.
            return doc.Nodes.Take(index + 1).Where(n => needed.Contains(n.Id)).ToList();
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
