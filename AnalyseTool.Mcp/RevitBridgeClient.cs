using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AnalyseTool.Mcp;

/// <summary>
/// WebSocket client to the in-Revit bridge (McpBridgeServer @ ws://127.0.0.1:&lt;port&gt;/). Owns one
/// connection, correlates responses by id, and exposes the two operations we need: list the live
/// command set and invoke a command. Connects lazily so the MCP server can be spawned before Revit
/// is running; the next call retries the connection.
/// </summary>
internal sealed class RevitBridgeClient : IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;

    public RevitBridgeClient(int port) => _uri = new Uri($"ws://127.0.0.1:{port}/");

    private bool IsConnected => _ws is { State: WebSocketState.Open };

    public async Task<JsonNode?> ListCommandsAsync(CancellationToken ct)
    {
        // Discovery must never hang: if the bridge doesn't answer quickly, fail fast so the MCP
        // server still finishes tools/list (with an empty list) instead of letting the AI client
        // time the WHOLE server out (~30s) and mark it disconnected.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendAsync(new JsonObject { ["type"] = "list" }, timeout.Token);
    }

    public async Task<JsonNode?> InvokeAsync(string command, JsonNode? payload, CancellationToken ct)
        => await SendAsync(new JsonObject { ["type"] = "invoke", ["command"] = command, ["payload"] = payload }, ct);

    private async Task<JsonNode?> SendAsync(JsonObject envelope, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        string id = Guid.NewGuid().ToString("N");
        envelope["id"] = id;

        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        byte[] bytes = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws!.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }

        await using (ct.Register(() => { if (_pending.TryRemove(id, out var t)) t.TrySetCanceled(); }))
            return await tcs.Task;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsConnected) return;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            _ws?.Dispose();
            var ws = new ClientWebSocket();
            // Bound the connect so a half-open / wrong listener on the port can't hang us.
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                await ws.ConnectAsync(_uri, connectTimeout.Token);
            }
            _ws = ws;
            _ = Task.Run(() => ReceiveLoopAsync(ws));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach Revit on {_uri}. Is Revit running with the AnalyseTool MCP server enabled (Settings)? ({ex.Message})");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws)
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        FailAllPending("Revit bridge closed the connection.");
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                Dispatch(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        catch
        {
            FailAllPending("Revit bridge connection error.");
        }
    }

    private void Dispatch(string text)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(text);
            string? id = node?["id"]?.GetValue<string>();
            if (id == null || !_pending.TryRemove(id, out var tcs)) return;

            if (node!["error"] is JsonNode err)
                tcs.TrySetException(new InvalidOperationException(err.GetValue<string>()));
            else
                tcs.TrySetResult(node["result"]?.DeepClone());
        }
        catch
        {
            // Malformed frame — ignore; the pending call will time out via cancellation.
        }
    }

    private void FailAllPending(string reason)
    {
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(new InvalidOperationException(reason));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws is { State: WebSocketState.Open })
            {
                // Bound the graceful close so shutdown never blocks on the close handshake.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
            }
        }
        catch { /* ignore */ }
        _ws?.Dispose();
    }
}
