using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;
using Serilog;

namespace AnalyseTool.Core.Common.Dispatch
{
    /// <summary>
    /// One command invocation as a transport hands it to the platform. Carries everything the
    /// platform needs to route, authorize and report the call — a transport never talks to the
    /// dispatcher directly.
    /// </summary>
    /// <param name="Command">Registered command name (extension commands are "&lt;id&gt;.&lt;name&gt;").</param>
    /// <param name="Payload">Raw JSON payload; JValue.CreateNull() when the command takes none.</param>
    /// <param name="Source">Transport identity for logging/telemetry/policy: "webview2", "mcp",
    /// "ribbon", a future "remote", …</param>
    internal sealed record CommandRequest(string Command, JToken Payload, string Source)
    {
        public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

        /// <summary>Progress sink bound by the transport to the originating caller (window, request id).</summary>
        public IProgress<ProgressInfo>? Progress { get; init; }

        /// <summary>Optional pre-execution gate: sees the resolved registration (name, ReadOnly,
        /// Destructive, …) and returns false to refuse. This is where a remote transport plugs in
        /// its user-consent step later; local transports leave it null.</summary>
        public Func<CommandRegistration, Task<bool>>? Gate { get; init; }

        // Room to grow (additive init-properties keep every existing caller compiling):
        //   - Priority for scheduling once the queue actually schedules
        //   - CallerIdentity once remote transports authenticate
    }

    /// <summary>
    /// THE single entry point through which every transport (WebView2 windows, the MCP bridge,
    /// future remote transports) reaches the platform.
    ///
    /// Not yet a scheduling queue: requests execute immediately and may overlap — actual Revit
    /// model access still serializes on the RevitTaskHub external event. The funnel exists so
    /// scheduling, priorities, consent gates and per-source policy can be added in ONE place
    /// without touching any transport. Adding a transport must require zero changes here.
    /// </summary>
    /// <summary>A command currently executing through the queue (for the busy indicator / MCP).
    /// <see cref="Progress"/> is the LAST report the command made, null until it made one — so an
    /// indicator that opens late still shows where the command is.</summary>
    internal sealed record RunningCommand(long Id, string Command, string Source, DateTime StartedUtc)
    {
        public ProgressInfo? Progress { get; init; }
    }

    internal sealed class CommandQueue
    {
        private readonly CommandDispatcher _dispatcher;

        // Observability: which commands are in flight RIGHT NOW (name, transport, started-at). The
        // queue doesn't schedule yet, but the user must be able to see WHY the tool is busy — both
        // in the UI (bottom status bar) and over MCP (an agent checks before piling more work on).
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, RunningCommand> _running = new();
        // The cancellation of each run, linked to the transport's own token: a transport cancels its
        // call through its token, a PERSON cancels it through TryCancel — the activity window's button,
        // which must work for a call that came over MCP and has no window of its own.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, CancellationTokenSource> _cancellations = new();
        private long _nextRunId;

        /// <summary>Introspection commands stay out of the registry — a status poll must not make the
        /// tool look busy (and must not re-trigger the event it is answering).</summary>
        private static readonly HashSet<string> Untracked = new(StringComparer.OrdinalIgnoreCase)
        {
            "GetQueueStatus",
        };

        public CommandQueue(CommandDispatcher dispatcher) => _dispatcher = dispatcher;

        /// <summary>Raised (on a worker thread) whenever a command starts or finishes.</summary>
        public event Action? RunningChanged;

        /// <summary>Raised (on the reporting thread) when a running command reports progress. The
        /// transport's own sink still gets every report; this is the copy for whoever else shows the
        /// platform's state — the host's activity window, the status snapshot.</summary>
        public event Action<RunningCommand, ProgressInfo>? ProgressReported;

        /// <summary>Cancels one running command by its run id (from <see cref="Running"/>). True when
        /// it was running and has been told; the command answers its caller as cancelled.</summary>
        public bool TryCancel(long runId)
        {
            if (!_cancellations.TryGetValue(runId, out CancellationTokenSource? cts)) return false;
            try { cts.Cancel(); } catch (ObjectDisposedException) { return false; }
            return true;
        }

        /// <summary>Snapshot of the commands in flight, oldest first.</summary>
        public IReadOnlyList<RunningCommand> Running =>
            _running.Values.OrderBy(r => r.StartedUtc).ToList();

        /// <summary>Registered commands, for transport-side introspection (MCP tools/list, the
        /// Settings "Commands" table). Read-only — registration stays a platform concern.</summary>
        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _dispatcher.RegisteredCommands;

        public bool IsRegistered(string command) => _dispatcher.IsRegistered(command);

        /// <summary>One command's metadata, or null when it is not registered. Callers that need more
        /// than "does it exist" — the ribbon deciding whether a button click can run a command or has
        /// to ask for its arguments first — would otherwise scan <see cref="RegisteredCommands"/>.</summary>
        public CommandRegistration? GetRegistration(string command) => _dispatcher.GetRegistration(command);

        public async Task<object?> ExecuteAsync(CommandRequest request)
        {
            if (request.Gate is not null)
            {
                // An unknown name has nothing to authorize: it falls through to the dispatcher, which
                // refuses it by name. Everything that CAN execute resolves a registration here first,
                // so no command reaches DispatchAsync without passing its transport's gate.
                CommandRegistration? registration = _dispatcher.GetRegistration(request.Command);
                if (registration is not null && !await request.Gate(registration).ConfigureAwait(false))
                    throw new UnauthorizedAccessException(
                        $"Command '{request.Command}' is not available over {request.Source}.");
            }

            bool track = !Untracked.Contains(request.Command);

            // Logged only for TRACKED commands, and Untracked already names the right set: a status
            // poll that must not make the tool look busy is the same poll that must not fill the log.
            // The indicator asks every couple of seconds, per open window, for as long as Revit runs —
            // one session measured ~840 of these lines around three real events, which does not make a
            // log verbose, it makes it unreadable, and a log nobody can read is the reason a command
            // that failed all evening was never diagnosed.
            if (track)
                Log.Debug("Command {Command} invoked via {Source}", request.Command, request.Source);

            long runId = 0;
            CancellationToken token = request.CancellationToken;
            IProgress<ProgressInfo>? progress = request.Progress;
            CancellationTokenSource? cancellation = null;
            if (track)
            {
                runId = Interlocked.Increment(ref _nextRunId);
                _running[runId] = new RunningCommand(runId, request.Command, request.Source, DateTime.UtcNow);
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
                _cancellations[runId] = cancellation;
                token = cancellation.Token;
                long id = runId;
                // Every report goes to the transport's sink AND to the queue's listeners; the last one
                // is kept on the RunningCommand for an observer that arrives late. Delivered on the
                // REPORTING thread, not posted: a command inside a Revit transaction reports from the
                // UI thread it is holding, and that is the one moment an indicator on that thread can
                // repaint — a posted callback would wait until the transaction ends.
                progress = new SynchronousProgress(info =>
                {
                    request.Progress?.Report(info);
                    if (_running.TryGetValue(id, out RunningCommand? current))
                    {
                        RunningCommand updated = current with { Progress = info };
                        _running[id] = updated;
                        try { ProgressReported?.Invoke(updated, info); }
                        catch (Exception ex) { Log.Warning(ex, "A ProgressReported subscriber threw"); }
                    }
                });
                NotifyRunningChanged();
            }
            try
            {
                return await _dispatcher
                    .DispatchAsync(request.Command, request.Payload, token, progress)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (track)
                {
                    _running.TryRemove(runId, out _);
                    _cancellations.TryRemove(runId, out _);
                    cancellation?.Dispose();
                    NotifyRunningChanged();
                }
            }
        }

        /// <summary>IProgress that calls back on the reporting thread — the opposite of Progress&lt;T&gt;,
        /// which posts to the captured context. Listeners that need another thread marshal themselves.</summary>
        private sealed class SynchronousProgress : IProgress<ProgressInfo>
        {
            private readonly Action<ProgressInfo> _report;
            public SynchronousProgress(Action<ProgressInfo> report) => _report = report;
            public void Report(ProgressInfo value) => _report(value);
        }

        private void NotifyRunningChanged()
        {
            try { RunningChanged?.Invoke(); }
            catch (Exception ex) { Log.Warning(ex, "A RunningChanged subscriber threw"); }
        }
    }
}
