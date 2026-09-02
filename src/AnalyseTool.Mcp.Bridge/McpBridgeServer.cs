using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Core.Features.Extensions;
using AnalyseTool.Core.Features.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AnalyseTool.Mcp.Bridge
{
    /// <summary>
    /// Second transport (beside WebView2Transport): a localhost TCP bridge that lets the out-of-process
    /// MCP server (AnalyseTool.Mcp.exe) reach the SAME CommandQueue.
    ///
    /// Plain TCP + JSON (accumulate bytes until one complete JSON value parses) — NO WebSocket
    /// handshake/framing, since we own both ends. Bound to 127.0.0.1 (no admin / url-acl). The MCP
    /// server connects once per request (connect → send → read → close), so each connection is a
    /// single request/response cycle (the loop also tolerates sequential request/response on one
    /// connection). This connect-per-request model removes all persistent-socket/reconnect fragility.
    ///
    /// Protocol (see McpWire for the whole shape): request { "id", "type", "command", "payload" };
    /// an invoke may be preceded by progress frames on its connection, and its outcome is kept as a
    /// job that "result" collects and "cancel" stops from another connection.
    ///           response { "id", "result": &lt;any&gt; } | { "id", "error": { code, message, hint? } }
    /// </summary>
    internal sealed class McpBridgeServer
    {
        private const string Source = "mcp";

        private readonly CommandQueue _queue;
        private readonly string _token;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        // Bumped on every extension reload. Together with the C# switch it is the "catalog stamp":
        // anything that changes what tools/list would answer changes the stamp (see McpWire.Catalog).
        private long _catalogVersion;

        // Every invoke is a JOB: it keeps running when the connection that asked for it is gone, and
        // its outcome is kept here so a "result" request can collect it later (#99). The same record
        // holds the CancellationTokenSource a "cancel" request trips (#109). Keyed by the caller's
        // request id, which the exe generates per call and can therefore quote back.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Job> _jobs = new();
        /// <summary>How long a finished outcome stays collectable. Long enough for an agent that lost
        /// its call to come back after the user noticed; short enough that a session's worth of
        /// results does not accumulate in Revit's memory.</summary>
        internal static readonly TimeSpan JobRetention = TimeSpan.FromHours(1);
        private const int MaxStoredJobs = 200;
        /// <summary>Progress frames are throttled to this: a command reporting every element of a
        /// thousand would otherwise write a thousand frames, and the client renders maybe four a second.</summary>
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

        public McpBridgeServer(CommandQueue queue, string token)
        {
            _queue = queue;
            _token = token ?? string.Empty;
            CoreServices.ExtensionsReloaded += () => Interlocked.Increment(ref _catalogVersion);
        }

        /// <summary>What the exe compares to learn that its tool list is stale. Reload counter plus the
        /// C# switch, because the switch hides a whole set of authoring tools without any reload.</summary>
        private string CatalogStamp =>
            $"{Interlocked.Read(ref _catalogVersion)}:{(CodeExecutionSettings.Enabled ? 1 : 0)}";

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }

        public void Start(int port)
        {
            if (IsRunning) return;

            CancellationTokenSource cts = new();
            TcpListener listener = new(IPAddress.Loopback, port);
            try
            {
                listener.Start();
            }
            catch
            {
                // Port in use, most likely. Leave no half-started state behind: Stop() early-returns
                // on !IsRunning, so anything stashed in the fields here would never be cleaned up.
                cts.Dispose();
                throw;
            }

            _cts = cts;
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            IsRunning = true;

            _ = AcceptLoopAsync(listener, cts.Token);
        }

        public void Stop()
        {
            IsRunning = false;

            CancellationTokenSource? cts = _cts;
            TcpListener? listener = _listener;
            _listener = null;
            _cts = null;
            Port = 0;

            try { cts?.Cancel(); } catch { /* ignore */ }
            try { listener?.Stop(); } catch { /* ignore */ }
            cts?.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                    {
                        break; // listener stopped / cancelled — the only reasons to leave the loop
                    }
                    catch (Exception ex)
                    {
                        // A single failed accept (a client that vanished mid-handshake, a transient
                        // socket error) used to end the loop for the whole session while IsRunning
                        // still said "running": Settings showed green, every agent call failed, and
                        // only a Revit restart brought it back.
                        Log.Warning(ex, "MCP bridge: accept failed; continuing to listen");
                        continue;
                    }

                    // Each connection handled independently; concurrency across connections is fine
                    // (the dispatcher marshals model access onto the single Revit thread anyway).
                    _ = HandleClientAsync(client, ct);
                }
            }
            finally
            {
                // Whatever ended the loop, this listener is no longer accepting — say so, so the status
                // in Settings cannot claim otherwise. The identity check keeps a lingering old loop
                // from clearing the flag of a server that has since been restarted.
                if (ReferenceEquals(_listener, listener))
                {
                    if (!ct.IsCancellationRequested)
                        Log.Warning("MCP bridge: accept loop ended unexpectedly");
                    IsRunning = false;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                {
                    client.NoDelay = true;
                    NetworkStream stream = client.GetStream();
                    // One writer at a time on this connection: a progress frame and the final reply must
                    // not interleave bytes, and progress arrives from whatever thread the command runs on.
                    using SemaphoreSlim writeLock = new(1, 1);
                    async Task WriteAsync(string json)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        await writeLock.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                            await stream.FlushAsync(ct).ConfigureAwait(false);
                        }
                        finally { writeLock.Release(); }
                    }

                    string? message;
                    while ((message = await ReadJsonAsync(stream, ct).ConfigureAwait(false)) != null)
                    {
                        string response = WithCatalog(await HandleMessageAsync(message, ct, WriteAsync).ConfigureAwait(false));
                        await WriteAsync(response).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // Never let a connection fault crash Revit on a background thread. A caller that went
                // away mid-command loses nothing: the job runs on and its outcome is stored (see Job).
            }
        }

        private async Task<string> HandleMessageAsync(string message, CancellationToken ct, Func<string, Task> sendFrame)
        {
            string? id = null;
            JObject? req = null; // outside the try so the failure log can name the command and payload
            try
            {
                req = JObject.Parse(message);
                id = (string?)req[McpWire.Id];

                // Loopback is not an authorization boundary: every process running as this user can
                // open this port, and what is behind it drives Revit. The token proves the caller was
                // configured by the user (Settings hands it out in the client config snippet).
                if (!IsAuthorized((string?)req[McpWire.Token]))
                    return Err(id, McpWire.Codes.Unauthorized,
                        "Missing or wrong bridge token.",
                        "Re-copy the MCP client configuration from AnalyseTool Settings — it includes a " +
                        "--token argument.");

                string type = (string?)req[McpWire.Type] ?? McpWire.TypeInvoke;

                // The stamp alone: the poller's request. No Revit thread, no command — answers in
                // microseconds, which is what lets the exe ask every few seconds without cost.
                if (string.Equals(type, McpWire.TypeVersion, StringComparison.OrdinalIgnoreCase))
                    return Ok(id, new JObject { [McpWire.Catalog] = CatalogStamp });

                if (string.Equals(type, McpWire.TypeList, StringComparison.OrdinalIgnoreCase))
                {
                    JArray commands = new(_queue.RegisteredCommands
                        .Where(IsAvailableToAi)
                        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(c => new JObject
                        {
                            [McpWire.Name] = c.Name,
                            [McpWire.SourceField] = c.Source,
                            [McpWire.Description] = c.Description,
                            [McpWire.ReadOnly] = c.ReadOnly,
                            [McpWire.Destructive] = c.Destructive,
                            // Compacted HERE, not at registration: a listing carries every command and is
                            // re-fetched on every reconnect, so a huge nested DTO costs on each one. The
                            // stored schema stays whole for callers that reason about it.
                            [McpWire.InputSchema] = JToken.Parse(SchemaListing.Compact(c.InputSchemaJson)),
                            [McpWire.OutputSchema] = JToken.Parse(SchemaListing.Compact(c.OutputSchemaJson)),
                        }));
                    return Ok(id, new JObject { [McpWire.Commands] = commands });
                }

                // The two verbs about OTHER calls. Neither touches Revit: one trips a token, the other
                // reads a record. Both answer in microseconds even while Revit is held, which is the
                // whole point — they are how a caller reaches a call it can no longer wait on.
                if (string.Equals(type, McpWire.TypeCancel, StringComparison.OrdinalIgnoreCase))
                    return Ok(id, new JObject { [McpWire.Cancelled] = CancelJob(id) });
                if (string.Equals(type, McpWire.TypeResult, StringComparison.OrdinalIgnoreCase))
                    return Ok(id, DescribeJob(id));

                string command = (string?)req[McpWire.Command] ?? string.Empty;
                JToken payload = req[McpWire.Payload] ?? JValue.CreateNull();

                // Checked HERE rather than in the command: the command deserializes with Newtonsoft, which
                // drops an unrecognised field without a word, so a misspelled filter comes back as an
                // unfiltered result that looks like a successful call.
                //
                // Validated against Compact() — the SAME schema tools/list published, not the stored one.
                // They differ for a command whose schema exceeded the listing cap and went out as
                // free-form: holding a caller to parameters it was never shown would be a rejection it
                // cannot act on, so those go through unchecked, exactly as they do today.
                // Named `registered`, not `registration`: the Gate lambda below already binds that name,
                // and C# refuses a lambda parameter that shadows an enclosing local.
                //
                // Filtered on ExposeToMcp, so a command the AI may NEVER call is indistinguishable from
                // one that does not exist. Looking it up unfiltered leaked it: a guessed name reached
                // the validator, which answered "invalid arguments" and listed the command's
                // parameters — telling an agent that SetCodeExecution exists and what it takes, which
                // is the one command HiddenFromMcp is load-bearing for.
                CommandRegistration? registered = _queue.RegisteredCommands
                    .Where(c => c.ExposeToMcp)
                    .FirstOrDefault(c => string.Equals(c.Name, command, StringComparison.OrdinalIgnoreCase));
                if (registered is null)
                {
                    // Answered here rather than left to the dispatcher, which can only say "not
                    // registered": the bridge knows the catalogue it published and can point at the name
                    // the caller probably meant. Suggestions come from the AI-VISIBLE names only —
                    // pointing an agent at a command it may not call is a worse answer than none.
                    string? nearest = NearestName.Closest(
                        command, _queue.RegisteredCommands.Where(IsAvailableToAi).Select(c => c.Name));
                    return Err(id, McpWire.Codes.UnknownCommand, $"No command named '{command}'.",
                        nearest is null
                            ? "Call tools/list for the commands this server offers."
                            : $"Did you mean '{nearest}'? Call tools/list for the full set.");
                }

                // Answered BEFORE the payload is validated. An authoring tool switched off is not a
                // secret — its name and schema are in the authoring guide and in tools/list whenever
                // the toggle is on — so it gets a sentence it can act on rather than an unknown-command
                // answer. But making the agent correct its arguments first, only to be refused for a
                // reason no argument can fix, is a retry loop with a known-useless end.
                if (!IsAvailableToAi(registered))
                    return Err(id, McpWire.Codes.NotAvailable,
                        $"'{command}' needs C# code execution, which is switched off in AnalyseTool Settings.",
                        "Only a person can enable it — ask the user to turn it on, then call tools/list again.");

                string? complaint = PayloadValidator.Validate(
                    command, SchemaListing.Compact(registered.InputSchemaJson), payload);
                if (complaint is not null)
                    return Err(id, McpWire.Codes.InvalidArguments, complaint,
                        "Nothing was executed. Correct the arguments and call again.");

                return await RunJobAsync(id ?? Guid.NewGuid().ToString("N"), command, payload, ct, sendFrame)
                    .ConfigureAwait(false);
            }
            // Classified by exception TYPE, never by matching the message text: a reworded sentence must
            // not silently reclassify an error that a caller branches on.
            catch (UnauthorizedAccessException ex)
            {
                return Err(id, McpWire.Codes.NotAvailable, ex.Message,
                    "This command is not exposed to AI callers. The C#-execution tools additionally " +
                    "require the toggle in AnalyseTool Settings.");
            }
            catch (OperationCanceledException)
            {
                return Err(id, McpWire.Codes.Cancelled, "The call was cancelled before it finished.");
            }
            catch (Exception ex)
            {
                return Failed(id, req, ex);
            }
        }

        /// <summary>
        /// Runs one invoke as a job: the outcome is recorded whether or not the caller is still there to
        /// receive it, progress goes out as interim frames while the connection lasts, and a cancel
        /// request from another connection reaches the command through the job's token.
        /// </summary>
        private async Task<string> RunJobAsync(string id, string command, JToken payload, CancellationToken ct, Func<string, Task> sendFrame)
        {
            PruneJobs();
            Job job = new(id, command, CancellationTokenSource.CreateLinkedTokenSource(ct));
            _jobs[id] = job;

            // Progress: a frame per report, throttled, on the caller's connection while it lasts. A write
            // to a caller that has gone is swallowed here — the job does not care who is listening.
            DateTime lastFrame = DateTime.MinValue;
            IProgress<Sdk.ProgressInfo> progress = new Progress<Sdk.ProgressInfo>(info =>
            {
                DateTime now = DateTime.UtcNow;
                if (info.Fraction < 1 && now - lastFrame < ProgressInterval) return;
                lastFrame = now;
                JObject frame = new()
                {
                    [McpWire.Id] = id,
                    [McpWire.Progress] = new JObject
                    {
                        [McpWire.ProgressFraction] = info.Fraction,
                        [McpWire.ProgressMessage] = info.Message,
                    },
                };
                _ = SendQuietlyAsync(sendFrame, frame.ToString(Formatting.None));
            });

            try
            {
                object? result = await _queue.ExecuteAsync(
                    new CommandRequest(command, payload, Source)
                    {
                        CancellationToken = job.Cancellation.Token,
                        Progress = progress,
                        // The SAME predicate that builds tools/list also gates invoke. Listing is a
                        // convenience; this is the access boundary. HiddenFromMcp is load-bearing —
                        // SetCodeExecution carries it precisely so the AI cannot grant itself code
                        // execution — and a name absent from tools/list is still a name an agent can
                        // guess, so filtering the catalogue alone protects nothing.
                        Gate = registration => Task.FromResult(IsAvailableToAi(registration)),
                    }).ConfigureAwait(false);
                JToken value = result is null ? JValue.CreateNull() : JToken.FromObject(result);
                job.Finish(McpWire.JobStates.Done, value, null);
                return Ok(id, value);
            }
            catch (OperationCanceledException)
            {
                job.Finish(McpWire.JobStates.Cancelled, null, ErrorObject(McpWire.Codes.Cancelled, "The call was cancelled before it finished.", null));
                return Err(id, McpWire.Codes.Cancelled, "The call was cancelled before it finished.");
            }
            catch (Exception ex)
            {
                string reply = Failed(id, new JObject { [McpWire.Command] = command, [McpWire.Payload] = payload }, ex);
                job.Finish(McpWire.JobStates.Failed, null, (JObject?)JObject.Parse(reply)[McpWire.Error]);
                return reply;
            }
            finally
            {
                job.Cancellation.Dispose();
            }
        }

        private static async Task SendQuietlyAsync(Func<string, Task> send, string json)
        {
            try { await send(json).ConfigureAwait(false); }
            catch { /* the caller is gone; the job runs on */ }
        }

        private bool CancelJob(string? id)
        {
            if (id is null || !_jobs.TryGetValue(id, out Job? job) || job.FinishedUtc is not null) return false;
            try { job.Cancellation.Cancel(); } catch (ObjectDisposedException) { return false; }
            return true;
        }

        private JObject DescribeJob(string? id)
        {
            if (id is null || !_jobs.TryGetValue(id, out Job? job))
                return new JObject { [McpWire.JobStatus] = McpWire.JobStates.Unknown };
            JObject answer = new()
            {
                [McpWire.JobStatus] = job.Status,
                [McpWire.JobCommand] = job.Command,
                [McpWire.JobSeconds] = Math.Round(((job.FinishedUtc ?? DateTime.UtcNow) - job.StartedUtc).TotalSeconds, 1),
            };
            if (job.Result is not null) answer[McpWire.JobResult] = job.Result;
            if (job.Error is not null) answer[McpWire.JobError] = job.Error;
            return answer;
        }

        /// <summary>Drops finished outcomes past the retention window, and the oldest beyond the cap.</summary>
        private void PruneJobs()
        {
            DateTime cutoff = DateTime.UtcNow - JobRetention;
            foreach (KeyValuePair<string, Job> entry in _jobs)
                if (entry.Value.FinishedUtc is { } finished && finished < cutoff)
                    _jobs.TryRemove(entry.Key, out _);
            if (_jobs.Count <= MaxStoredJobs) return;
            foreach (Job stale in _jobs.Values.Where(j => j.FinishedUtc is not null).OrderBy(j => j.FinishedUtc).Take(_jobs.Count - MaxStoredJobs))
                _jobs.TryRemove(stale.Id, out _);
        }

        /// <summary>One invoke and what became of it. Finish() is called exactly once, by the runner.</summary>
        private sealed class Job
        {
            public Job(string id, string command, CancellationTokenSource cancellation)
            {
                Id = id;
                Command = command;
                Cancellation = cancellation;
            }

            public string Id { get; }
            public string Command { get; }
            public CancellationTokenSource Cancellation { get; }
            public DateTime StartedUtc { get; } = DateTime.UtcNow;
            public DateTime? FinishedUtc { get; private set; }
            public string Status { get; private set; } = McpWire.JobStates.Running;
            public JToken? Result { get; private set; }
            public JObject? Error { get; private set; }

            public void Finish(string status, JToken? result, JObject? error)
            {
                Status = status;
                Result = result;
                Error = error;
                FinishedUtc = DateTime.UtcNow;
            }
        }

        /// <summary>The command-failed reply, and the log line that goes with it.</summary>
        private static string Failed(string? id, JObject? req, Exception ex)
        {
            // The exception arrives marshalled off the Revit thread, so the OUTER one is regularly a
            // wrapper ("One or more errors occurred.") and the sentence that says what broke sits
            // in InnerException. Walk to the root, log the whole chain, and send the root's type and
            // message: an error that lives nowhere — not in the reply, not in the log — cost a whole
            // Revit session per bug to isolate (#97).
            Exception root = ex;
            while (root.InnerException is not null) root = root.InnerException;
            string commandName = (string?)req?[McpWire.Command] ?? "?";
            Log.Error(ex, "MCP: command {Command} failed — {ExceptionType}: {Message}. Payload: {Payload}",
                commandName, root.GetType().Name, root.Message, Abbreviate(req?[McpWire.Payload]));
            return Err(id, McpWire.Codes.CommandFailed,
                $"{root.GetType().Name}: {root.Message}",
                ReferenceEquals(root, ex) ? null : $"Outer exception: {ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>The payload for the log line: enough to reproduce, not enough to flood.</summary>
        private static string Abbreviate(JToken? payload)
        {
            string text = payload?.ToString(Formatting.None) ?? "null";
            return text.Length <= 500 ? text : text.Substring(0, 500) + "…";
        }

        /// <summary>Constant-time token comparison. An empty configured token means the host could not
        /// persist one (unwritable profile folder); refuse rather than fall open.</summary>
        private bool IsAuthorized(string? presented)
        {
            if (string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(presented)) return false;

            byte[] expected = Encoding.UTF8.GetBytes(_token);
            byte[] actual = Encoding.UTF8.GetBytes(presented);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        /// <summary>
        /// Whether a registered command may be reached by the AI at all — the one rule shared by
        /// tools/list and invoke, so the two can never drift apart:
        /// <list type="bullet">
        /// <item><c>HiddenFromMcp</c> commands are local plugin management, never AI tools;</item>
        /// <item>the code-authoring tools additionally require the Settings toggle (they hard-refuse
        /// when off anyway — this keeps them out of reach instead of merely out of sight).</item>
        /// </list>
        /// </summary>
        private static bool IsAvailableToAi(CommandRegistration command) =>
            command.ExposeToMcp && (CodeExecutionSettings.Enabled || !IsCodeAuthoringTool(command.Name));

        /// <summary>
        /// The tools behind the Settings toggle. Not only the two that RUN code: reading a script's
        /// source hands the AI code off the user's machine, and reloading is the step that makes written
        /// code take effect — the toggle is the user saying "I am authoring code here with AI", and each
        /// of these is part of doing that. The toggle itself stays out of reach: SetCodeExecution is
        /// HiddenFromMcp, so only a person at the Settings page turns this on or off.
        /// </summary>
        private static bool IsCodeAuthoringTool(string name) =>
            string.Equals(name, ExecuteRevitCode.CommandName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, SaveAsCommand.CommandName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, GetScriptSource.CommandName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, SaveExtensionUi.CommandName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, UpdateExtensionManifest.CommandName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ReloadExtensionsCommand.CommandName, StringComparison.OrdinalIgnoreCase);

        private static string Ok(string? id, JToken result) =>
            new JObject { [McpWire.Id] = id, [McpWire.Result] = result }.ToString(Formatting.None);

        /// <summary>Adds the catalog stamp to a finished reply. Done once, here, on the way out, so no
        /// branch of HandleMessageAsync can forget it — an error reply carries it too, and a reply to
        /// SaveAsCommand is precisely the one that must say "the list just changed".</summary>
        private string WithCatalog(string response)
        {
            try
            {
                JObject reply = JObject.Parse(response);
                reply[McpWire.Catalog] = CatalogStamp;
                return reply.ToString(Formatting.None);
            }
            catch (JsonException)
            {
                return response;
            }
        }

        /// <summary>An error the caller can act on: a code to branch on, a message to read, and — only
        /// when there is something useful to say — what to do about it.</summary>
        private static string Err(string? id, string code, string message, string? hint = null) =>
            new JObject { [McpWire.Id] = id, [McpWire.Error] = ErrorObject(code, message, hint) }.ToString(Formatting.None);

        private static JObject ErrorObject(string code, string message, string? hint)
        {
            JObject error = new()
            {
                [McpWire.ErrorCode] = code,
                [McpWire.ErrorMessage] = message,
            };
            if (!string.IsNullOrWhiteSpace(hint)) error[McpWire.ErrorHint] = hint;
            return error;
        }

        /// <summary>Requests are command payloads, not file uploads — anything larger is a mistake or an
        /// attack, and reading it costs the Revit process its memory.</summary>
        private const int MaxMessageBytes = 8 * 1024 * 1024;

        /// <summary>
        /// Reads bytes until the accumulated buffer is one complete JSON value (handles a request that
        /// spans several TCP reads). Returns null on EOF / connection close.
        /// </summary>
        private static async Task<string?> ReadJsonAsync(NetworkStream stream, CancellationToken ct)
        {
            using MemoryStream ms = new();
            byte[] buffer = new byte[8192];
            JsonBoundaryScanner scanner = new();

            while (true)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }

                if (read == 0) return null; // EOF
                ms.Write(buffer, 0, read);

                // Structural scan, resuming where the last read stopped: O(total bytes) for the whole
                // message. The previous version decoded the entire buffer to a string and ran
                // JToken.Parse in a try/catch after EVERY 8 KB read — quadratic work plus an exception
                // per read, so a few megabytes of junk from any local process pegged a core and
                // allocated gigabytes.
                if (scanner.ConsumedCompleteValue(ms.GetBuffer(), (int)ms.Length))
                    return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

                if (ms.Length > MaxMessageBytes) return null;
            }
        }

        /// <summary>
        /// Tracks JSON nesting depth across reads to spot the end of the top-level value. Strings and
        /// escapes are honoured so braces inside a payload string don't confuse the depth count. This
        /// answers "is the message complete", not "is it valid" — parsing still decides that.
        /// </summary>
        private struct JsonBoundaryScanner
        {
            private int _offset;      // bytes already scanned
            private int _depth;       // open { and [
            private bool _inString;
            private bool _escaped;

            public bool ConsumedCompleteValue(byte[] buffer, int length)
            {
                for (; _offset < length; _offset++)
                {
                    char c = (char)buffer[_offset];

                    if (_inString)
                    {
                        if (_escaped) _escaped = false;
                        else if (c == '\\') _escaped = true;
                        else if (c == '"') _inString = false;
                        continue;
                    }

                    switch (c)
                    {
                        case '"': _inString = true; break;
                        case '{':
                        case '[': _depth++; break;
                        case '}':
                        case ']':
                            if (--_depth <= 0)
                            {
                                _offset++;
                                return true;
                            }
                            break;
                    }
                }

                // The protocol only ever sends objects, so anything that hasn't closed its outermost
                // bracket yet is simply an incomplete read — keep waiting for more bytes.
                return false;
            }
        }
    }
}
