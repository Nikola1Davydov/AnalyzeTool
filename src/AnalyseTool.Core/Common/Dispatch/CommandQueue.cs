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
    /// <summary>A command currently executing through the queue (for the busy indicator / MCP).</summary>
    internal sealed record RunningCommand(long Id, string Command, string Source, DateTime StartedUtc);

    internal sealed class CommandQueue
    {
        private readonly CommandDispatcher _dispatcher;

        // Observability: which commands are in flight RIGHT NOW (name, transport, started-at). The
        // queue doesn't schedule yet, but the user must be able to see WHY the tool is busy — both
        // in the UI (bottom status bar) and over MCP (an agent checks before piling more work on).
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, RunningCommand> _running = new();
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

        /// <summary>Snapshot of the commands in flight, oldest first.</summary>
        public IReadOnlyList<RunningCommand> Running =>
            _running.Values.OrderBy(r => r.StartedUtc).ToList();

        /// <summary>Registered commands, for transport-side introspection (MCP tools/list, the
        /// Settings "Commands" table). Read-only — registration stays a platform concern.</summary>
        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _dispatcher.RegisteredCommands;

        public bool IsRegistered(string command) => _dispatcher.IsRegistered(command);

        public async Task<object?> ExecuteAsync(CommandRequest request)
        {
            if (request.Gate is not null)
            {
                CommandRegistration? registration = _dispatcher.GetRegistration(request.Command);
                if (registration is not null && !await request.Gate(registration).ConfigureAwait(false))
                    throw new OperationCanceledException(
                        $"Command '{request.Command}' was refused by the {request.Source} transport gate.");
            }

            Log.Debug("Command {Command} invoked via {Source}", request.Command, request.Source);

            bool track = !Untracked.Contains(request.Command);
            long runId = 0;
            if (track)
            {
                runId = Interlocked.Increment(ref _nextRunId);
                _running[runId] = new RunningCommand(runId, request.Command, request.Source, DateTime.UtcNow);
                NotifyRunningChanged();
            }
            try
            {
                return await _dispatcher
                    .DispatchAsync(request.Command, request.Payload, request.CancellationToken, request.Progress)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (track)
                {
                    _running.TryRemove(runId, out _);
                    NotifyRunningChanged();
                }
            }
        }

        private void NotifyRunningChanged()
        {
            try { RunningChanged?.Invoke(); }
            catch (Exception ex) { Log.Warning(ex, "A RunningChanged subscriber threw"); }
        }
    }
}
