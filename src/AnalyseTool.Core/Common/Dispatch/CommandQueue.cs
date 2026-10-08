using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;

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

        // The execution pipeline, outermost first: Logging → Gating → Tracking → Dispatching. Each
        // concern is one decorator (CommandPipeline.cs); a new one (caching, per-source policy,
        // scheduling) is one more line here. Logging sits OUTSIDE the gate so a refusal is logged too.
        private readonly ICommandExecutor _pipeline;

        // Observability: which commands are in flight RIGHT NOW (name, transport, started-at). The
        // queue doesn't schedule yet, but the user must be able to see WHY the tool is busy — both
        // in the UI (bottom status bar) and over MCP (an agent checks before piling more work on).
        private readonly TrackingExecutor _tracking;

        public CommandQueue(CommandDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _tracking = new TrackingExecutor(new DispatchingExecutor(dispatcher));
            _pipeline = new LoggingExecutor(new GatingExecutor(_tracking, dispatcher.GetRegistration));
        }

        /// <summary>Raised (on a worker thread) whenever a command starts or finishes.</summary>
        public event Action? RunningChanged
        {
            add => _tracking.RunningChanged += value;
            remove => _tracking.RunningChanged -= value;
        }

        /// <summary>Raised (on the reporting thread) when a running command reports progress. The
        /// transport's own sink still gets every report; this is the copy for whoever else shows the
        /// platform's state — the host's activity window, the status snapshot.</summary>
        public event Action<RunningCommand, ProgressInfo>? ProgressReported
        {
            add => _tracking.ProgressReported += value;
            remove => _tracking.ProgressReported -= value;
        }

        /// <summary>Cancels one running command by its run id (from <see cref="Running"/>). True when
        /// it was running and has been told; the command answers its caller as cancelled.</summary>
        public bool TryCancel(long runId) => _tracking.TryCancel(runId);

        /// <summary>Snapshot of the commands in flight, oldest first.</summary>
        public IReadOnlyList<RunningCommand> Running => _tracking.Running;

        /// <summary>Registered commands, for transport-side introspection (MCP tools/list, the
        /// Settings "Commands" table). Read-only — registration stays a platform concern.</summary>
        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _dispatcher.RegisteredCommands;

        public bool IsRegistered(string command) => _dispatcher.IsRegistered(command);

        /// <summary>One command's metadata, or null when it is not registered. Callers that need more
        /// than "does it exist" — the ribbon deciding whether a button click can run a command or has
        /// to ask for its arguments first — would otherwise scan <see cref="RegisteredCommands"/>.</summary>
        public CommandRegistration? GetRegistration(string command) => _dispatcher.GetRegistration(command);

        public Task<object?> ExecuteAsync(CommandRequest request) => _pipeline.ExecuteAsync(request);
    }
}
