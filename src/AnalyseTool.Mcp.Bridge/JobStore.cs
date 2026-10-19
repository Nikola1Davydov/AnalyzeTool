using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace AnalyseTool.Mcp.Bridge
{
    /// <summary>
    /// The bridge's invokes as jobs (#99, #109): each keeps running when its connection is gone, its
    /// outcome stays collectable for a while, and its token is reachable by a cancel from another
    /// connection. Keyed by the caller's request id, which the exe generates per call and can quote back.
    /// Separate from McpBridgeServer so the rules are testable without a socket or a Revit.
    /// </summary>
    internal sealed class JobStore
    {
        private readonly ConcurrentDictionary<string, Job> _jobs = new();
        private readonly TimeSpan _retention;
        private readonly int _maxStored;
        private readonly long _maxResultChars;
        private readonly TimeProvider _time;
        private long _resultChars;

        /// <summary>The default cap on stored results, in characters of their JSON. Every outcome stays
        /// in Revit's memory until it expires — including the many a caller already received, because
        /// whether a reply really arrived cannot be told from this side — so the total is what is bounded.</summary>
        public const long DefaultMaxResultChars = 16 * 1024 * 1024;

        public JobStore(TimeSpan retention, int maxStored, TimeProvider? time = null, long maxResultChars = DefaultMaxResultChars)
        {
            _retention = retention;
            _maxStored = maxStored;
            _maxResultChars = maxResultChars;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>Registers a new job, or returns null when <paramref name="id"/> is taken. Overwriting
        /// would orphan the running job's token, and a cancel could no longer reach it.</summary>
        public Job? TryStart(string id, string command, CancellationTokenSource cancellation)
        {
            Prune();
            Job job = new(id, command, cancellation, _time.GetUtcNow().UtcDateTime);
            if (_jobs.TryAdd(id, job)) return job;
            cancellation.Dispose();
            return null;
        }

        public void Finish(Job job, string status, JToken? result, JObject? error)
        {
            long size = (result?.ToString(Newtonsoft.Json.Formatting.None).Length ?? 0)
                        + (error?.ToString(Newtonsoft.Json.Formatting.None).Length ?? 0);
            // Counted BEFORE the outcome is published: pruning removes only finished jobs and subtracts
            // their size, so a prune can never subtract a size that was not added yet. A running job is
            // never pruned, so it is still stored here.
            Interlocked.Add(ref _resultChars, size);
            job.Finish(new Outcome(status, result, error, _time.GetUtcNow().UtcDateTime, size));
            // Pruned here too, not only when the next call starts: an idle bridge should not sit on an
            // hour of results it has already been told it may drop.
            Prune();
        }

        public bool Cancel(string? id)
        {
            if (id is null || !_jobs.TryGetValue(id, out Job? job) || job.Outcome is not null) return false;
            try { job.Cancellation.Cancel(); } catch (ObjectDisposedException) { return false; }
            return true;
        }

        public JObject Describe(string? id)
        {
            if (id is null || !_jobs.TryGetValue(id, out Job? job))
                return new JObject { [McpWire.JobStatus] = McpWire.JobStates.Unknown };

            // One read of the outcome: status, result and error always come from the same moment.
            Outcome? outcome = job.Outcome;
            JObject answer = new()
            {
                [McpWire.JobStatus] = outcome?.Status ?? McpWire.JobStates.Running,
                [McpWire.JobCommand] = job.Command,
                [McpWire.JobSeconds] = Seconds(job, outcome),
            };
            if (outcome?.Result is not null) answer[McpWire.JobResult] = outcome.Result;
            if (outcome?.Error is not null) answer[McpWire.JobError] = outcome.Error;
            return answer;
        }

        /// <summary>The recent calls, newest first, without their results — enough to pick the one whose
        /// handle was lost and ask for it by id.</summary>
        public JObject List()
        {
            JArray jobs = new(_jobs.Values
                .OrderByDescending(j => j.StartedUtc)
                .Take(50)
                .Select(j =>
                {
                    Outcome? outcome = j.Outcome;
                    return new JObject
                    {
                        [McpWire.Id] = j.Id,
                        [McpWire.JobStatus] = outcome?.Status ?? McpWire.JobStates.Running,
                        [McpWire.JobCommand] = j.Command,
                        [McpWire.JobSeconds] = Seconds(j, outcome),
                    };
                }));
            return new JObject { [McpWire.Jobs] = jobs };
        }

        /// <summary>Drops finished outcomes past the retention window, then the oldest finished ones while
        /// there are more than the count cap or their results exceed the size cap. Running jobs stay.</summary>
        internal void Prune()
        {
            DateTime cutoff = _time.GetUtcNow().UtcDateTime - _retention;
            foreach (Job job in _jobs.Values)
                if (job.Outcome is { } done && done.FinishedUtc < cutoff)
                    Remove(job);

            if (_jobs.Count <= _maxStored && Interlocked.Read(ref _resultChars) <= _maxResultChars) return;
            foreach (Job oldest in _jobs.Values.Where(j => j.Outcome is not null).OrderBy(j => j.Outcome!.FinishedUtc).ToList())
            {
                if (_jobs.Count <= _maxStored && Interlocked.Read(ref _resultChars) <= _maxResultChars) break;
                Remove(oldest);
            }
        }

        private void Remove(Job job)
        {
            if (_jobs.TryRemove(new KeyValuePair<string, Job>(job.Id, job)) && job.Outcome is { } done)
                Interlocked.Add(ref _resultChars, -done.Size);
        }

        internal int Count => _jobs.Count;
        internal long ResultChars => Interlocked.Read(ref _resultChars);

        private double Seconds(Job job, Outcome? outcome) =>
            Math.Round(((outcome?.FinishedUtc ?? _time.GetUtcNow().UtcDateTime) - job.StartedUtc).TotalSeconds, 1);

        /// <summary>How a job ended. Published as ONE object: a reader on another connection sees either
        /// no outcome or all of it — never "done" with the result not yet written.</summary>
        internal sealed record Outcome(string Status, JToken? Result, JObject? Error, DateTime FinishedUtc, long Size = 0);

        /// <summary>One invoke and what became of it. Finished exactly once, by the runner.</summary>
        internal sealed class Job
        {
            private Outcome? _outcome;

            public Job(string id, string command, CancellationTokenSource cancellation, DateTime startedUtc)
            {
                Id = id;
                Command = command;
                Cancellation = cancellation;
                StartedUtc = startedUtc;
            }

            public string Id { get; }
            public string Command { get; }
            public CancellationTokenSource Cancellation { get; }
            public DateTime StartedUtc { get; }
            public Outcome? Outcome => Volatile.Read(ref _outcome);

            internal void Finish(Outcome outcome) => Volatile.Write(ref _outcome, outcome);
        }
    }
}
