using AnalyseTool.Core.Common.Pipelines;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AnalyseTool.Core.Tests
{
    /// <summary>
    /// The engine against a fake dispatcher — no Revit, no queue, no document. Every test here pins a
    /// decision that was argued about rather than a line that happens to exist, which is why the names
    /// read as claims.
    /// </summary>
    [TestFixture]
    public sealed class PipelineEngineTests
    {
        /// <summary>Records what it was asked to run and answers from a script, so a test can make one
        /// specific node throw without any of the others knowing.</summary>
        private sealed class FakeDispatcher : INodeDispatcher
        {
            private readonly Func<string, JToken, object?> _handler;

            public FakeDispatcher(Func<string, JToken, object?> handler) => _handler = handler;

            public List<(string Command, JToken Payload)> Calls { get; } = new();

            public Task<object?> ExecuteAsync(string command, JToken payload, CancellationToken ct)
            {
                Calls.Add((command, payload));
                return Task.FromResult(_handler(command, payload));
            }
        }

        private static PipelineNode Node(string id, string command, NodeFailureAction onFailure = NodeFailureAction.Stop)
            => new() { Id = id, Command = command, OnFailure = onFailure };

        private static PipelineDocument Pipeline(params PipelineNode[] nodes)
            => new() { Id = "test", Name = "test", Nodes = nodes.ToList() };

        [Test]
        public async Task Nodes_run_in_file_order()
        {
            FakeDispatcher dispatcher = new((_, _) => null);
            PipelineDocument doc = Pipeline(Node("n1", "A"), Node("n2", "B"), Node("n3", "C"));

            PipelineRunResult result = await new PipelineEngine(dispatcher).RunAsync(doc);

            Assert.That(result.State, Is.EqualTo(RunState.Completed));
            Assert.That(dispatcher.Calls.Select(c => c.Command), Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public async Task Binding_passes_a_field_of_an_earlier_result_into_the_payload()
        {
            FakeDispatcher dispatcher = new((command, _) =>
                command == "A" ? new { ids = new[] { 1, 2, 3 } } : null);

            PipelineNode consumer = Node("n2", "B");
            consumer.Bind = new Dictionary<string, string> { ["elementIds"] = "n1.ids" };

            await new PipelineEngine(dispatcher).RunAsync(Pipeline(Node("n1", "A"), consumer));

            JToken payload = dispatcher.Calls[1].Payload;
            Assert.That(payload["elementIds"]!.ToObject<int[]>(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public async Task A_binding_beats_a_literal_of_the_same_name()
        {
            // A literal left over from before the node was connected must not win over the wire it is now
            // fed by — otherwise connecting a node silently does nothing.
            FakeDispatcher dispatcher = new((command, _) => command == "A" ? new { value = "fresh" } : null);

            PipelineNode consumer = Node("n2", "B");
            consumer.Params = new JObject { ["value"] = "stale" };
            consumer.Bind = new Dictionary<string, string> { ["value"] = "n1.value" };

            await new PipelineEngine(dispatcher).RunAsync(Pipeline(Node("n1", "A"), consumer));

            Assert.That(dispatcher.Calls[1].Payload["value"]!.Value<string>(), Is.EqualTo("fresh"));
        }

        [Test]
        public async Task A_wildcard_binding_collects_every_match_and_splices_arrays()
        {
            // The case the first real run hit: filter a list, then act on ALL of it. Without wildcards a
            // binding can only ever reach item [0], which silently acts on one row out of many.
            FakeDispatcher dispatcher = new((command, _) => command == "A"
                ? new
                {
                    items = new[]
                    {
                        new { failingElements = new[] { 2825, 2991 } },
                        new { failingElements = new[] { 2849, 3020 } },
                    },
                }
                : null);

            PipelineNode consumer = Node("n2", "B");
            consumer.Bind = new Dictionary<string, string> { ["elementIds"] = "n1.items[*].failingElements" };

            await new PipelineEngine(dispatcher).RunAsync(Pipeline(Node("n1", "A"), consumer));

            Assert.That(dispatcher.Calls[1].Payload["elementIds"]!.ToObject<int[]>(),
                Is.EqualTo(new[] { 2825, 2991, 2849, 3020 }),
                "the two id arrays must arrive as one flat list, not as a list of lists");
        }

        [Test]
        public async Task A_wildcard_that_matches_nothing_fails_instead_of_binding_an_empty_list()
        {
            // An empty list would let a mutating command report a cheerful "0 written" while the pipeline
            // was in fact built on a shape that does not exist.
            FakeDispatcher dispatcher = new((command, _) => command == "A" ? new { items = Array.Empty<object>() } : null);

            PipelineNode consumer = Node("n2", "B");
            consumer.Bind = new Dictionary<string, string> { ["elementIds"] = "n1.items[*].failingElements" };

            PipelineRunResult result = await new PipelineEngine(dispatcher)
                .RunAsync(Pipeline(Node("n1", "A"), consumer));

            Assert.That(result.State, Is.EqualTo(RunState.Failed));
            Assert.That(result.Nodes[1].Error, Does.Contain("matched nothing"));
            Assert.That(dispatcher.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task An_unresolvable_binding_fails_the_node_instead_of_binding_null()
        {
            // Passing null into a mutating command is the failure worth being loud about.
            FakeDispatcher dispatcher = new((_, _) => new { });

            PipelineNode consumer = Node("n2", "B");
            consumer.Bind = new Dictionary<string, string> { ["x"] = "n1.missing" };

            PipelineRunResult result = await new PipelineEngine(dispatcher)
                .RunAsync(Pipeline(Node("n1", "A"), consumer));

            Assert.That(result.State, Is.EqualTo(RunState.Failed));
            Assert.That(result.Nodes[1].Error, Does.Contain("missing"));
            Assert.That(dispatcher.Calls, Has.Count.EqualTo(1), "the failing node must not have been dispatched");
        }

        [Test]
        public async Task A_failing_node_stops_the_run_by_default()
        {
            FakeDispatcher dispatcher = new((command, _) =>
                command == "B" ? throw new InvalidOperationException("boom") : null);

            PipelineRunResult result = await new PipelineEngine(dispatcher)
                .RunAsync(Pipeline(Node("n1", "A"), Node("n2", "B"), Node("n3", "C")));

            Assert.That(result.State, Is.EqualTo(RunState.Failed));
            Assert.That(result.StoppedAt, Is.EqualTo("n2"));
            Assert.That(dispatcher.Calls.Select(c => c.Command), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public async Task OnFailure_continue_carries_on_but_the_run_is_still_not_a_success()
        {
            FakeDispatcher dispatcher = new((command, _) =>
                command == "B" ? throw new InvalidOperationException("boom") : null);

            PipelineRunResult result = await new PipelineEngine(dispatcher).RunAsync(
                Pipeline(Node("n1", "A"), Node("n2", "B", NodeFailureAction.Continue), Node("n3", "C")));

            Assert.That(dispatcher.Calls.Select(c => c.Command), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(result.State, Is.EqualTo(RunState.Failed),
                "a run that swallowed a failure has not succeeded, whatever the node was told to do");
        }

        [Test]
        public async Task Cancellation_ends_the_run_even_on_a_node_marked_continue()
        {
            // THE test. Cancellation reaches the engine as an exception, exactly like a real failure, so a
            // single catch consulting OnFailure would let a node marked Continue swallow the user's Stop —
            // and Stop would then quietly stop working on precisely the nodes someone marked as skippable.
            FakeDispatcher dispatcher = new((command, _) =>
                command == "B" ? throw new OperationCanceledException() : null);

            PipelineRunResult result = await new PipelineEngine(dispatcher).RunAsync(
                Pipeline(Node("n1", "A"), Node("n2", "B", NodeFailureAction.Continue), Node("n3", "C")));

            Assert.That(result.State, Is.EqualTo(RunState.Cancelled));
            Assert.That(dispatcher.Calls.Select(c => c.Command), Is.EqualTo(new[] { "A", "B" }),
                "nothing may run after a cancellation");
        }

        [Test]
        public async Task The_result_says_how_far_the_run_got()
        {
            // Stop ends a run; it does not undo one. Which node was reached is what tells the user how
            // much of the model was already written.
            FakeDispatcher dispatcher = new((command, _) =>
                command == "C" ? throw new InvalidOperationException("boom") : null);

            PipelineRunResult result = await new PipelineEngine(dispatcher)
                .RunAsync(Pipeline(Node("n1", "A"), Node("n2", "B"), Node("n3", "C")));

            Assert.That(result.Nodes.Select(n => n.State), Is.EqualTo(new[]
            {
                NodeState.Completed, NodeState.Completed, NodeState.Failed,
            }));
            Assert.That(result.StoppedAt, Is.EqualTo("n3"));
        }

        /// <summary>Delivers inline. <see cref="Progress{T}"/> posts to a synchronization context, which
        /// makes any assertion about what arrived a race rather than a test.</summary>
        private sealed class SyncProgress<T> : IProgress<T>
        {
            public List<T> Reports { get; } = new();
            public void Report(T value) => Reports.Add(value);
        }

        [Test]
        public async Task Every_node_reports_that_it_started_and_how_it_ended()
        {
            FakeDispatcher dispatcher = new((command, _) =>
                command == "B" ? throw new InvalidOperationException("boom") : null);
            SyncProgress<NodeProgress> progress = new();

            await new PipelineEngine(dispatcher).RunAsync(
                Pipeline(Node("n1", "A"), Node("n2", "B")), progress);

            Assert.That(progress.Reports.Select(r => (r.NodeId, r.State)), Is.EqualTo(new[]
            {
                ("n1", NodeState.Executing), ("n1", NodeState.Completed),
                ("n2", NodeState.Executing), ("n2", NodeState.Failed),
            }));
            Assert.That(progress.Reports[^1].Error, Is.EqualTo("boom"));
        }
    }
}
