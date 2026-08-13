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
    /// Protocol: request { "id", "type":"invoke"|"list", "command", "payload" }
    ///           response { "id", "result": &lt;any&gt; } | { "id", "error": { code, message, hint? } }
    /// </summary>
    internal sealed class McpBridgeServer
    {
        private const string Source = "mcp";

        private readonly CommandQueue _queue;
        private readonly string _token;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public McpBridgeServer(CommandQueue queue, string token)
        {
            _queue = queue;
            _token = token ?? string.Empty;
        }

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

                    string? message;
                    while ((message = await ReadJsonAsync(stream, ct).ConfigureAwait(false)) != null)
                    {
                        string response = await HandleMessageAsync(message, ct).ConfigureAwait(false);
                        byte[] bytes = Encoding.UTF8.GetBytes(response);
                        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                        await stream.FlushAsync(ct).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // Never let a connection fault crash Revit on a background thread.
            }
        }

        private async Task<string> HandleMessageAsync(string message, CancellationToken ct)
        {
            string? id = null;
            try
            {
                JObject req = JObject.Parse(message);
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
                CommandRegistration? registered = _queue.RegisteredCommands
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

                string? complaint = PayloadValidator.Validate(
                    command, SchemaListing.Compact(registered.InputSchemaJson), payload);
                if (complaint is not null)
                    return Err(id, McpWire.Codes.InvalidArguments, complaint,
                        "Nothing was executed. Correct the arguments and call again.");

                object? result = await _queue.ExecuteAsync(
                    new CommandRequest(command, payload, Source)
                    {
                        CancellationToken = ct,
                        // The SAME predicate that builds tools/list also gates invoke. Listing is a
                        // convenience; this is the access boundary. HiddenFromMcp is load-bearing —
                        // SetCodeExecution carries it precisely so the AI cannot grant itself code
                        // execution — and a name absent from tools/list is still a name an agent can
                        // guess, so filtering the catalogue alone protects nothing.
                        Gate = registration => Task.FromResult(IsAvailableToAi(registration)),
                    }).ConfigureAwait(false);
                return Ok(id, result is null ? JValue.CreateNull() : JToken.FromObject(result));
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
                return Err(id, McpWire.Codes.CommandFailed, ex.Message);
            }
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
            string.Equals(name, ReloadExtensionsCommand.CommandName, StringComparison.OrdinalIgnoreCase);

        private static string Ok(string? id, JToken result) =>
            new JObject { [McpWire.Id] = id, [McpWire.Result] = result }.ToString(Formatting.None);

        /// <summary>An error the caller can act on: a code to branch on, a message to read, and — only
        /// when there is something useful to say — what to do about it.</summary>
        private static string Err(string? id, string code, string message, string? hint = null)
        {
            JObject error = new()
            {
                [McpWire.ErrorCode] = code,
                [McpWire.ErrorMessage] = message,
            };
            if (!string.IsNullOrWhiteSpace(hint)) error[McpWire.ErrorHint] = hint;

            return new JObject { [McpWire.Id] = id, [McpWire.Error] = error }.ToString(Formatting.None);
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
