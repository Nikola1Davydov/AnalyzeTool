using Autodesk.Revit.UI;
using Serilog;
using System.Collections.Concurrent;

namespace AnalyseTool.Core.Common.Dispatch
{
    /// <summary>
    /// Central marshaller onto the Revit thread. One long-lived <see cref="ExternalEvent"/> is
    /// created once (in a valid API context, from Bootstrap); callers enqueue synchronous work from
    /// any thread and await the result. Replaces the single-slot ExternalEventHub for the new
    /// dispatch path.
    /// </summary>
    internal sealed class RevitTaskHub : IExternalEventHandler
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();
        private ExternalEvent? _event;

        // Waiting-detection: an ExternalEvent only fires when Revit is IDLE. If the user sits in a
        // modal dialog or an edit mode, raised work waits forever and the tool looks frozen — track
        // how long the oldest un-served work has been waiting so the UI can say WHY nothing happens.
        private int _pending;                 // work items enqueued but not yet executed
        private long _pendingSinceTicks;      // UTC ticks since the queue became non-empty / last item ran
        private bool _executing;              // a work item is running RIGHT NOW (Revit thread is ours)

        // TEMPORARY — instrumentation for issue #88. The whole design here assumes Execute is never
        // re-entered; a modal dialog raised inside a work item runs a nested message pump, and whether
        // Revit can raise the ExternalEvent again from inside it is exactly the open question. Counting
        // only — no behaviour change, deliberately: a guard put in before the answer would be a lock in
        // the hottest path of dispatch, defending against something that may not happen.
        private int _depth;

        /// <summary>The session's single hub, for status introspection (GetQueueStatus). Never used
        /// for dispatching — commands still receive the hub through their context.</summary>
        public static RevitTaskHub? Current { get; private set; }

        /// <summary>Must be called from a valid Revit API context (e.g. inside IExternalCommand.Execute).</summary>
        public void Initialize()
        {
            _event ??= ExternalEvent.Create(this);
            Current = this;
        }

        public Task<T> EnqueueAsync<T>(Func<UIApplication, T> work)
        {
            if (_event is null)
                throw new InvalidOperationException("RevitTaskHub is not initialized.");

            // RunContinuationsAsynchronously: the awaiting continuation must not run inline on the
            // Revit thread inside Execute (it could re-enter or block the event handler).
            TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            // Count BEFORE enqueuing: the other way round, Execute could dequeue and decrement the item
            // before this increment landed, driving _pending to -1. The counter then never hit the
            // "== 1" reset again and GetQueueStatus measured waitingSeconds from a stale timestamp —
            // reporting "Revit is busy" at idle, on the very signal agents are told to trust.
            if (Interlocked.Increment(ref _pending) == 1)
                Interlocked.Exchange(ref _pendingSinceTicks, DateTime.UtcNow.Ticks);

            _queue.Enqueue(app =>
            {
                try { tcs.SetResult(work(app)); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            _event.Raise();
            return tcs.Task;
        }

        public void Execute(UIApplication app)
        {
            // TEMPORARY — issue #88. depth > 1 means Revit re-entered us while a work item was still on
            // the stack (almost certainly through a modal dialog's nested message pump). If that line
            // never appears in a session that DID sit on a modal, the guard is unnecessary and we can
            // say so with evidence. Note the second symptom to watch for if it does fire: the inner
            // call's finally clears _executing while the outer item is still running, so GetQueueStatus
            // would start reporting "waitingForUser" during our own work.
            int depth = Interlocked.Increment(ref _depth);
            if (depth > 1)
                Log.Warning("EXP88: RevitTaskHub.Execute RE-ENTERED, depth={Depth}, thread={Thread}, pending={Pending}",
                    depth, Environment.CurrentManagedThreadId, Volatile.Read(ref _pending));

            Volatile.Write(ref _executing, true);
            try
            {
                while (_queue.TryDequeue(out Action<UIApplication>? item))
                {
                    item(app);
                    // Work is flowing — restart the clock so "waiting" measures a stall, not throughput.
                    if (Interlocked.Decrement(ref _pending) == 0)
                        Interlocked.Exchange(ref _pendingSinceTicks, 0);
                    else
                        Interlocked.Exchange(ref _pendingSinceTicks, DateTime.UtcNow.Ticks);
                }
            }
            finally
            {
                Volatile.Write(ref _executing, false);
                Interlocked.Decrement(ref _depth); // TEMPORARY — issue #88
            }
        }


        /// <summary>How many Revit-thread work items are waiting, for how long the OLDEST one has been
        /// waiting (0 when work is flowing), and whether a work item is executing right now.
        /// Pending work that waits while NOTHING is executing means Revit cannot go idle — the user is
        /// in a modal dialog / edit mode or a native Revit command is running. Waiting while executing
        /// just means our current work item is long, which is a different (benign) story.</summary>
        public (int Pending, double WaitingSeconds, bool Executing) Status
        {
            get
            {
                int pending = Volatile.Read(ref _pending);
                long since = Interlocked.Read(ref _pendingSinceTicks);
                double seconds = pending > 0 && since > 0
                    ? (DateTime.UtcNow - new DateTime(since, DateTimeKind.Utc)).TotalSeconds
                    : 0;
                return (pending, seconds, Volatile.Read(ref _executing));
            }
        }

        public string GetName() => "AnalyseTool.RevitTaskHub";
    }
}
