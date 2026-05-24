using AnalyseTool.Common.Dispatch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace AnalyseTool.Common.Transport
{
    /// <summary>
    /// Third transport (next to WebView2Transport): a localhost WebSocket bridge that lets the
    /// out-of-process MCP server (AnalyseTool.Mcp.exe) reach the SAME CommandDispatcher. The MCP
    /// server speaks MCP over stdio with the AI client and forwards each tool call here.
    ///
    /// Hosted on a raw <see cref="TcpListener"/> bound to 127.0.0.1 (no HTTP url-acl / admin needed,
    /// unlike HttpListener) with a hand-rolled WebSocket upgrade handshake, then driven through
    /// <see cref="WebSocket.CreateFromStream"/>. Protocol is a tiny JSON envelope we own on both ends:
    ///   request  : { "id": "...", "type": "invoke"|"list", "command": "...", "payload": &lt;any&gt; }
    ///   response : { "id": "...", "result": &lt;any&gt; }  |  { "id": "...", "error": "message" }
    /// </summary>
    internal sealed class McpBridgeServer
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

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
                    break; // listener stopped or cancelled
                }

                // One connection at a time is the normal case (a single MCP server), but tolerate
                // more by handling each on its own task.
                _ = HandleClientAsync(client, ct);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    if (!await TryWebSocketHandshakeAsync(stream, ct).ConfigureAwait(false))
                        return;

                    using WebSocket ws = WebSocket.CreateFromStream(
                        stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));

                    await PumpAsync(ws, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // Never let a connection fault crash Revit on a background thread.
            }
        }

        private async Task PumpAsync(WebSocket ws, CancellationToken ct)
        {
            byte[] buffer = new byte[8192];
            SemaphoreSlim sendLock = new(1, 1); // WebSocket sends must not overlap

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                string? message = await ReceiveTextAsync(ws, buffer, ct).ConfigureAwait(false);
                if (message == null) break; // close or error

                // Handle each message on its own task so a slow command (e.g. a long Revit op or AI
                // call) can't block subsequent messages — including "list". Receiving stays
                // sequential (buffer reuse is safe); only dispatch + reply run concurrently.
                _ = HandleAndReplyAsync(ws, message, sendLock, ct);
            }
        }

        private async Task HandleAndReplyAsync(WebSocket ws, string message, SemaphoreSlim sendLock, CancellationToken ct)
        {
            try
            {
                string response = await HandleMessageAsync(message, ct).ConfigureAwait(false);
                byte[] bytes = Encoding.UTF8.GetBytes(response);

                await sendLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                            .ConfigureAwait(false);
                }
                finally
                {
                    sendLock.Release();
                }
            }
            catch
            {
                // Never let a reply failure crash the connection / Revit on a background thread.
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

        private static async Task<string?> ReceiveTextAsync(WebSocket ws, byte[] buffer, CancellationToken ct)
        {
            using MemoryStream ms = new();
            WebSocketReceiveResult result;
            do
            {
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct).ConfigureAwait(false);
                    }
                    catch { /* ignore */ }
                    return null;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>
        /// Reads the client's HTTP upgrade request and writes the 101 Switching Protocols response,
        /// computing Sec-WebSocket-Accept. Reads exactly up to the header terminator so no frame
        /// bytes are consumed before WebSocket.CreateFromStream takes over.
        /// </summary>
        private static async Task<bool> TryWebSocketHandshakeAsync(NetworkStream stream, CancellationToken ct)
        {
            string headers = await ReadHttpHeadersAsync(stream, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headers)) return false;

            string? key = null;
            foreach (string line in headers.Split("\r\n"))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (line.AsSpan(0, colon).Trim().Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                {
                    key = line[(colon + 1)..].Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(key)) return false;

            string accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));

            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";

            byte[] bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }

        private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
        {
            using MemoryStream ms = new();
            byte[] one = new byte[1];
            int terminatorMatch = 0; // matches against "\r\n\r\n"

            while (terminatorMatch < 4)
            {
                int read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
                if (read == 0) return string.Empty; // connection closed

                byte b = one[0];
                ms.WriteByte(b);

                bool expectedCr = terminatorMatch is 0 or 2;
                if (expectedCr) terminatorMatch = b == (byte)'\r' ? terminatorMatch + 1 : 0;
                else terminatorMatch = b == (byte)'\n' ? terminatorMatch + 1 : 0;

                if (ms.Length > 16 * 1024) return string.Empty; // header too large; bail
            }

            return Encoding.ASCII.GetString(ms.ToArray());
        }
    }
}
