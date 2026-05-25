using AnalyseTool.Common.Dispatch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AnalyseTool.Common.Transport
{
    /// <summary>
    /// Third transport (beside WebView2Transport): a localhost TCP bridge that lets the out-of-process
    /// MCP server (AnalyseTool.Mcp.exe) reach the SAME CommandDispatcher.
    ///
    /// Plain TCP + JSON (accumulate bytes until one complete JSON value parses) — NO WebSocket
    /// handshake/framing, since we own both ends. Bound to 127.0.0.1 (no admin / url-acl). The MCP
    /// server connects once per request (connect → send → read → close), so each connection is a
    /// single request/response cycle (the loop also tolerates sequential request/response on one
    /// connection). This connect-per-request model removes all persistent-socket/reconnect fragility.
    ///
    /// Protocol: request { "id", "type":"invoke"|"list", "command", "payload" }
    ///           response { "id", "result": &lt;any&gt; } | { "id", "error": "message" }
    /// </summary>
    internal sealed class McpBridgeServer
    {
        private readonly CommandDispatcher _dispatcher;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public McpBridgeServer(CommandDispatcher dispatcher) => _dispatcher = dispatcher;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }

        public void Start(int port)
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            IsRunning = true;

            _ = AcceptLoopAsync(_listener, _cts.Token);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
            _cts = null;
            Port = 0;
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    break; // listener stopped / cancelled
                }

                // Each connection handled independently; concurrency across connections is fine
                // (the dispatcher marshals model access onto the single Revit thread anyway).
                _ = HandleClientAsync(client, ct);
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
                id = (string?)req["id"];
                string type = (string?)req["type"] ?? "invoke";

                if (string.Equals(type, "list", StringComparison.OrdinalIgnoreCase))
                {
                    JArray commands = new(_dispatcher.RegisteredCommands
                        .Where(c => c.ExposeToMcp)
                        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(c => new JObject
                        {
                            ["name"] = c.Name,
                            ["source"] = c.Source,
                            ["description"] = c.Description,
                            ["readOnly"] = c.ReadOnly,
                            ["destructive"] = c.Destructive,
                            ["inputSchema"] = JToken.Parse(c.InputSchemaJson),
                        }));
                    return Ok(id, new JObject { ["commands"] = commands });
                }

                string command = (string?)req["command"] ?? string.Empty;
                JToken payload = req["payload"] ?? JValue.CreateNull();

                object? result = await _dispatcher.DispatchAsync(command, payload, ct).ConfigureAwait(false);
                return Ok(id, result is null ? JValue.CreateNull() : JToken.FromObject(result));
            }
            catch (Exception ex)
            {
                return Err(id, ex.Message);
            }
        }

        private static string Ok(string? id, JToken result) =>
            new JObject { ["id"] = id, ["result"] = result }.ToString(Formatting.None);

        private static string Err(string? id, string message) =>
            new JObject { ["id"] = id, ["error"] = message }.ToString(Formatting.None);

        /// <summary>
        /// Reads bytes until the accumulated buffer is one complete JSON value (handles a request that
        /// spans several TCP reads). Returns null on EOF / connection close.
        /// </summary>
        private static async Task<string?> ReadJsonAsync(NetworkStream stream, CancellationToken ct)
        {
            using MemoryStream ms = new();
            byte[] buffer = new byte[8192];

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

                string text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                if (IsCompleteJson(text)) return text;

                if (ms.Length > 32 * 1024 * 1024) return null; // sanity cap
            }
        }

        private static bool IsCompleteJson(string text)
        {
            try { JToken.Parse(text); return true; }
            catch { return false; }
        }
    }
}
