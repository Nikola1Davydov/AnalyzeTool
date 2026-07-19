using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AnalyseTool.Mcp;

/// <summary>
/// Talks to the in-Revit bridge (McpBridgeServer) over plain TCP at 127.0.0.1:&lt;port&gt;.
/// Connect-per-request: every call opens a fresh TCP connection, sends one JSON request, reads the
/// full JSON response (accumulate until it parses), then closes. No persistent socket, no reconnect
/// logic, no id-correlation — one request/response per connection. This is the robust, dead-simple
/// model used by mcp-servers-for-revit; it removes the stale-connection / handshake fragility we hit
/// with a long-lived WebSocket.
/// </summary>
internal sealed class RevitBridgeClient
{
    private const string Host = "127.0.0.1";
    private readonly int _port;

    public RevitBridgeClient(int port) => _port = port;

    public async Task<JsonNode?> ListCommandsAsync(CancellationToken ct)
    {
        // Discovery must never hang the whole MCP server: fail fast so tools/list completes (empty)
        // instead of letting the AI client time the server out (~30s) and mark it disconnected.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeList }, timeout.Token);
    }

    public Task<JsonNode?> InvokeAsync(string command, JsonNode? payload, CancellationToken ct)
        => SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeInvoke, [McpWire.Command] = command, [McpWire.Payload] = payload }, ct);

    private async Task<JsonNode?> SendAsync(JsonObject envelope, CancellationToken ct)
    {
        envelope[McpWire.Id] = Guid.NewGuid().ToString("N");

        using var client = new TcpClient();

        // Connect (bounded) — a fresh connection each time, so there's no stale socket to recover.
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await client.ConnectAsync(Host, _port, connectTimeout.Token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot reach Revit on {Host}:{_port}. Is Revit running with the AnalyseTool MCP server enabled (Settings)? ({ex.Message})");
            }
        }

        client.NoDelay = true;
        using NetworkStream stream = client.GetStream();

        byte[] bytes = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);

        string responseText = await ReadJsonAsync(stream, ct);
        JsonNode? node = JsonNode.Parse(responseText);
        if (node?[McpWire.Error] is JsonNode err)
            throw new InvalidOperationException(err.GetValue<string>());
        return node?[McpWire.Result]?.DeepClone();
    }

    /// <summary>Reads bytes until the buffer is one complete JSON value (the response may span
    /// several TCP reads and be large).</summary>
    private static async Task<string> ReadJsonAsync(NetworkStream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        byte[] buffer = new byte[8192];

        while (true)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                if (ms.Length == 0)
                    throw new InvalidOperationException("Revit closed the connection without a response.");
                break; // EOF — return what we have (best effort)
            }

            ms.Write(buffer, 0, read);
            string text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            if (IsCompleteJson(text)) return text;

            if (ms.Length > 64 * 1024 * 1024)
                throw new InvalidOperationException("Response too large from Revit bridge.");
        }

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static bool IsCompleteJson(string text)
    {
        try { JsonNode.Parse(text); return true; }
        catch { return false; }
    }
}
