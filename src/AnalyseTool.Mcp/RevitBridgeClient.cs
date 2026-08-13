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

    /// <summary>
    /// Upper bound on ONE command invocation. A blocked Revit — a modal dialog, an edit mode, a native
    /// command — cannot serve the request until it goes idle again, and the bridge has no way to say so
    /// mid-flight, so an unbounded read waits forever and takes the agent's call with it.
    /// Deliberately generous: purging thousands of families or an AI round-trip legitimately takes
    /// minutes, and a timeout that fires on real work is worse than none. This only catches "never".
    /// </summary>
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromMinutes(10);

    private readonly int _port;
    private readonly string _token;

    public RevitBridgeClient(int port, string token)
    {
        _port = port;
        _token = token;
    }

    public async Task<JsonNode?> ListCommandsAsync(CancellationToken ct)
    {
        // Discovery must never hang the whole MCP server: fail fast so tools/list completes (empty)
        // instead of letting the AI client time the server out (~30s) and mark it disconnected.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeList }, timeout.Token);
    }

    public async Task<JsonNode?> InvokeAsync(string command, JsonNode? payload, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InvokeTimeout);

        try
        {
            return await SendAsync(
                new JsonObject
                {
                    [McpWire.Type] = McpWire.TypeInvoke,
                    [McpWire.Command] = command,
                    [McpWire.Payload] = payload,
                },
                timeout.Token);
        }
        // Only OUR deadline is turned into a diagnostic. When the caller cancelled, the cancellation is
        // theirs and must propagate as one — reporting it as a timeout would blame Revit for a Ctrl-C.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BridgeException(McpWire.Codes.Timeout,
                $"'{command}' did not answer within {InvokeTimeout.TotalMinutes:0} minutes. Revit is most " +
                "likely blocked by a modal dialog or an edit mode.",
                "Call GetQueueStatus — it answers even then — and retry once waitingForUser is false.");
        }
    }

    private async Task<JsonNode?> SendAsync(JsonObject envelope, CancellationToken ct)
    {
        envelope[McpWire.Id] = Guid.NewGuid().ToString("N");
        // The bridge rejects anything without the session token (see McpWire).
        envelope[McpWire.Token] = _token;

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
                throw new BridgeException(McpWire.Codes.RevitUnreachable,
                    $"Cannot reach Revit on {Host}:{_port}. ({ex.Message})",
                    "Start Revit and enable the AnalyseTool MCP server in Settings.");
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
            throw ToException(err);
        return node?[McpWire.Result]?.DeepClone();
    }

    /// <summary>
    /// Turns the wire's error into a typed one. The shape is { code, message, hint? }; a bare string is
    /// still accepted, because tolerating it costs one branch and turns a hypothetical version skew
    /// between the two halves into a readable message instead of a cast exception.
    /// </summary>
    private static BridgeException ToException(JsonNode err)
    {
        if (err is JsonObject error)
        {
            string code = error[McpWire.ErrorCode]?.GetValue<string>() ?? McpWire.Codes.CommandFailed;
            string message = error[McpWire.ErrorMessage]?.GetValue<string>() ?? "The call failed.";
            return new BridgeException(code, message, error[McpWire.ErrorHint]?.GetValue<string>());
        }

        return new BridgeException(McpWire.Codes.CommandFailed, err.ToString());
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
