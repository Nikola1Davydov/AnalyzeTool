using AnalyseTool.Mcp;
using AnalyseTool.Mcp.Bridge;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests;

/// <summary>The bridge's job records: a reused id must not orphan a running job, an outcome is seen
/// whole or not at all, and retention and the cap prune only finished jobs.</summary>
public class JobStoreTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Test]
    public async Task A_reused_id_is_refused_and_the_running_job_stays_cancellable()
    {
        JobStore store = new(TimeSpan.FromHours(1), 200);
        CancellationTokenSource first = new();
        store.TryStart("a", "GetElements", first);

        JobStore.Job? second = store.TryStart("a", "SetDataToParameters", new CancellationTokenSource());

        using (Assert.Multiple())
        {
            await Assert.That(second).IsNull();
            await Assert.That(store.Cancel("a")).IsTrue();
            await Assert.That(first.IsCancellationRequested).IsTrue();
            await Assert.That((string?)store.Describe("a")[McpWire.JobCommand]).IsEqualTo("GetElements");
        }
    }

    [Test]
    public async Task A_finished_job_is_described_with_its_result()
    {
        JobStore store = new(TimeSpan.FromHours(1), 200);
        JobStore.Job job = store.TryStart("a", "GetElements", new CancellationTokenSource())!;
        await Assert.That((string?)store.Describe("a")[McpWire.JobStatus]).IsEqualTo(McpWire.JobStates.Running);

        store.Finish(job, McpWire.JobStates.Done, new JObject { ["count"] = 4 }, null);
        JObject described = store.Describe("a");

        using (Assert.Multiple())
        {
            await Assert.That((string?)described[McpWire.JobStatus]).IsEqualTo(McpWire.JobStates.Done);
            await Assert.That((int?)described[McpWire.JobResult]?["count"]).IsEqualTo(4);
            await Assert.That(store.Cancel("a")).IsFalse();
        }
    }

    [Test]
    public async Task Done_is_never_seen_without_its_result()
    {
        // The old record wrote Status, then Result: a reader in between saw "done" with no result and
        // handed the agent null. The outcome is one object now; hammer it from two threads.
        JobStore store = new(TimeSpan.FromHours(1), 10_000);
        bool torn = false;
        for (int i = 0; i < 2_000 && !torn; i++)
        {
            string id = "j" + i;
            JobStore.Job job = store.TryStart(id, "X", new CancellationTokenSource())!;
            Task reader = Task.Run(() =>
            {
                for (int k = 0; k < 50; k++)
                {
                    JObject d = store.Describe(id);
                    if ((string?)d[McpWire.JobStatus] == McpWire.JobStates.Done && d[McpWire.JobResult] is null) torn = true;
                }
            });
            store.Finish(job, McpWire.JobStates.Done, new JValue(i), null);
            await reader;
        }

        await Assert.That(torn).IsFalse();
    }

    [Test]
    public async Task Pruning_drops_expired_and_excess_finished_jobs_but_never_running_ones()
    {
        ManualTime time = new();
        JobStore store = new(TimeSpan.FromHours(1), 2, time);
        JobStore.Job old = store.TryStart("old", "X", new CancellationTokenSource())!;
        store.Finish(old, McpWire.JobStates.Done, new JValue(1), null);
        store.TryStart("running", "X", new CancellationTokenSource());

        time.Now += TimeSpan.FromHours(2);
        store.Prune();

        using (Assert.Multiple())
        {
            await Assert.That((string?)store.Describe("old")[McpWire.JobStatus]).IsEqualTo(McpWire.JobStates.Unknown);
            await Assert.That((string?)store.Describe("running")[McpWire.JobStatus]).IsEqualTo(McpWire.JobStates.Running);
        }

        for (int i = 0; i < 3; i++)
        {
            JobStore.Job j = store.TryStart("f" + i, "X", new CancellationTokenSource())!;
            store.Finish(j, McpWire.JobStates.Done, new JValue(i), null);
            time.Now += TimeSpan.FromSeconds(1);
        }
        store.Prune();

        using (Assert.Multiple())
        {
            await Assert.That(store.Count).IsEqualTo(2);
            await Assert.That((string?)store.Describe("running")[McpWire.JobStatus]).IsEqualTo(McpWire.JobStates.Running);
            await Assert.That((string?)store.Describe("f2")[McpWire.JobStatus]).IsEqualTo(McpWire.JobStates.Done);
        }
    }
}
