using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AnalyseTool.Core.Common.Dispatch
{
    /// <summary>
    /// One stage of command execution. The <see cref="CommandQueue"/> runs every request through a
    /// chain of these — decorators around the dispatcher, each owning exactly one concern:
    /// <code>Logging → Gating → Tracking → Dispatching</code>
    /// A new cross-cutting concern (caching, per-source policy, scheduling) is one more decorator in
    /// <see cref="CommandQueue"/>'s constructor; no transport and no other stage changes. A stage that
    /// needs to change the request (the tracker swaps in its own token and progress sink) passes a
    /// copy made with <c>with</c> — the record the transport built is never mutated.
    /// </summary>
    internal interface ICommandExecutor
    {
        Task<object?> ExecuteAsync(CommandRequest request);
    }

    /// <summary>Shared rules of the pipeline stages.</summary>
    internal static class CommandPipeline
    {
        /// <summary>Introspection commands stay out of the registry AND out of the log — a status poll
        /// must not make the tool look busy (and must not re-trigger the event it is answering). The
        /// indicator asks every couple of seconds, per open window, for as long as Revit runs — one
        /// session measured ~840 such lines around three real events, which does not make a log
        /// verbose, it makes it unreadable.</summary>
        private static readonly HashSet<string> Quiet = new(StringComparer.OrdinalIgnoreCase)
        {
            "GetQueueStatus",
        };

        public static bool IsQuiet(string command) => Quiet.Contains(command);
    }

    /// <summary>The innermost stage: hands the request to the dispatcher.</summary>
    internal sealed class DispatchingExecutor : ICommandExecutor
    {
        private readonly CommandDispatcher _dispatcher;

        public DispatchingExecutor(CommandDispatcher dispatcher) => _dispatcher = dispatcher;

        public Task<object?> ExecuteAsync(CommandRequest request) =>
            _dispatcher.DispatchAsync(request.Command, request.Payload, request.CancellationToken, request.Progress);
    }

    /// <summary>
    /// Applies the transport's pre-execution gate. An unknown name has nothing to authorize: it falls
    /// through to the dispatcher, which refuses it by name. Everything that CAN execute resolves a
    /// registration here first, so no command reaches the dispatcher without passing its gate.
    /// </summary>
    internal sealed class GatingExecutor : ICommandExecutor
    {
        private readonly ICommandExecutor _inner;
        private readonly Func<string, CommandRegistration?> _resolve;

        public GatingExecutor(ICommandExecutor inner, Func<string, CommandRegistration?> resolve)
        {
            _inner = inner;
            _resolve = resolve;
        }

        public async Task<object?> ExecuteAsync(CommandRequest request)
        {
            if (request.Gate is not null)
            {
                CommandRegistration? registration = _resolve(request.Command);
                if (registration is not null && !await request.Gate(registration).ConfigureAwait(false))
                    throw new UnauthorizedAccessException(
                        $"Command '{request.Command}' is not available over {request.Source}.");
            }
            return await _inner.ExecuteAsync(request).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One log line per command OUTCOME: finished (with duration), cancelled, refused, or failed (with
    /// the whole exception, the root cause's type and message, and the payload abbreviated). Before this
    /// stage the queue only logged "invoked" at Debug, and a command that failed from a WebView window
    /// left nothing in the log at all — the transport sends <c>ex.Message</c> to the page and drops the
    /// rest. Every transport gets the same lines now, without logging a thing itself.
    /// </summary>
    internal sealed class LoggingExecutor : ICommandExecutor
    {
        private const int MaxPayloadChars = 500;

        private readonly ICommandExecutor _inner;
        private readonly ILogger? _logger;

        /// <param name="logger">Null = the global <see cref="Log.Logger"/>, read per call rather than
        /// captured, so a queue built before Serilog is configured still logs once it is.</param>
        public LoggingExecutor(ICommandExecutor inner, ILogger? logger = null)
        {
            _inner = inner;
            _logger = logger;
        }

        private ILogger Logger => _logger ?? Log.Logger;

        public async Task<object?> ExecuteAsync(CommandRequest request)
        {
            if (CommandPipeline.IsQuiet(request.Command))
                return await _inner.ExecuteAsync(request).ConfigureAwait(false);

            Logger.Debug("Command {Command} invoked via {Source}", request.Command, request.Source);
            long started = Stopwatch.GetTimestamp();
            try
            {
                object? result = await _inner.ExecuteAsync(request).ConfigureAwait(false);
                Logger.Information("Command {Command} via {Source} finished in {Elapsed} ms",
                    request.Command, request.Source, ElapsedMs(started));
                return result;
            }
            catch (OperationCanceledException)
            {
                // The caller asked for this — not a fault, and not worth a stack trace.
                Logger.Information("Command {Command} via {Source} cancelled after {Elapsed} ms",
                    request.Command, request.Source, ElapsedMs(started));
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                // A gate refusal or an endpoint rejecting credentials: the message says which, and the
                // stack trace adds nothing to either.
                Logger.Warning("Command {Command} via {Source} refused: {Message}",
                    request.Command, request.Source, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                // The exception arrives marshalled off the Revit thread, so the OUTER one is regularly a
                // wrapper ("One or more errors occurred.") and the sentence that says what broke sits in
                // InnerException. Log the whole chain, and name the root in the message template.
                Exception root = Root(ex);
                Logger.Error(ex, "Command {Command} via {Source} failed after {Elapsed} ms — {ExceptionType}: {Message}. Payload: {Payload}",
                    request.Command, request.Source, ElapsedMs(started),
                    root.GetType().Name, root.Message, Abbreviate(request.Payload));
                throw;
            }
        }

        private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        /// <summary>The innermost exception — the one whose message says what actually broke.</summary>
        public static Exception Root(Exception ex)
        {
            while (ex.InnerException is not null) ex = ex.InnerException;
            return ex;
        }

        /// <summary>The payload for a log line: enough to reproduce, not enough to flood.</summary>
        public static string Abbreviate(JToken? payload)
        {
            string text = payload?.ToString(Formatting.None) ?? "null";
            return text.Length <= MaxPayloadChars ? text : text.Substring(0, MaxPayloadChars) + "…";
        }
    }

    /// <summary>
    /// Keeps the registry of commands in flight (for the busy indicator and <c>GetQueueStatus</c>),
    /// links a per-run cancellation a PERSON can trigger (<see cref="TryCancel"/> — the activity
    /// window's button, which must work for a call that came over MCP and has no window of its own),
    /// and fans progress out to the queue's listeners.
    /// </summary>
    internal sealed class TrackingExecutor : ICommandExecutor
    {
        private readonly ICommandExecutor _inner;
        private readonly ConcurrentDictionary<long, RunningCommand> _running = new();
        private readonly ConcurrentDictionary<long, CancellationTokenSource> _cancellations = new();
        private long _nextRunId;

        public TrackingExecutor(ICommandExecutor inner) => _inner = inner;

        /// <summary>Raised (on a worker thread) whenever a command starts or finishes.</summary>
        public event Action? RunningChanged;

        /// <summary>Raised (on the reporting thread) when a running command reports progress.</summary>
        public event Action<RunningCommand, ProgressInfo>? ProgressReported;

        /// <summary>Snapshot of the commands in flight, oldest first.</summary>
        public IReadOnlyList<RunningCommand> Running =>
            _running.Values.OrderBy(r => r.StartedUtc).ToList();

        public bool TryCancel(long runId)
        {
            if (!_cancellations.TryGetValue(runId, out CancellationTokenSource? cts)) return false;
            try { cts.Cancel(); } catch (ObjectDisposedException) { return false; }
            return true;
        }

        public async Task<object?> ExecuteAsync(CommandRequest request)
        {
            if (CommandPipeline.IsQuiet(request.Command))
                return await _inner.ExecuteAsync(request).ConfigureAwait(false);

            long runId = Interlocked.Increment(ref _nextRunId);
            _running[runId] = new RunningCommand(runId, request.Command, request.Source, DateTime.UtcNow);
            // Linked to the transport's own token: a transport cancels its call through its token, a
            // person through TryCancel.
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            _cancellations[runId] = cancellation;

            // Every report goes to the transport's sink AND to the listeners; the last one is kept on the
            // RunningCommand for an observer that arrives late. Delivered on the REPORTING thread, not
            // posted: a command inside a Revit transaction reports from the UI thread it is holding, and
            // that is the one moment an indicator on that thread can repaint — a posted callback would
            // wait until the transaction ends.
            IProgress<ProgressInfo> progress = new SynchronousProgress(info =>
            {
                request.Progress?.Report(info);
                if (_running.TryGetValue(runId, out RunningCommand? current))
                {
                    RunningCommand updated = current with { Progress = info };
                    _running[runId] = updated;
                    try { ProgressReported?.Invoke(updated, info); }
                    catch (Exception ex) { Log.Warning(ex, "A ProgressReported subscriber threw"); }
                }
            });
            NotifyRunningChanged();
            try
            {
                return await _inner
                    .ExecuteAsync(request with { CancellationToken = cancellation.Token, Progress = progress })
                    .ConfigureAwait(false);
            }
            finally
            {
                _running.TryRemove(runId, out _);
                _cancellations.TryRemove(runId, out _);
                cancellation.Dispose();
                NotifyRunningChanged();
            }
        }

        private void NotifyRunningChanged()
        {
            try { RunningChanged?.Invoke(); }
            catch (Exception ex) { Log.Warning(ex, "A RunningChanged subscriber threw"); }
        }

        /// <summary>IProgress that calls back on the reporting thread — the opposite of Progress&lt;T&gt;,
        /// which posts to the captured context. Listeners that need another thread marshal themselves.</summary>
        private sealed class SynchronousProgress : IProgress<ProgressInfo>
        {
            private readonly Action<ProgressInfo> _report;
            public SynchronousProgress(Action<ProgressInfo> report) => _report = report;
            public void Report(ProgressInfo value) => _report(value);
        }
    }
}
