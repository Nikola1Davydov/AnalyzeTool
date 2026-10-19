using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AnalyseTool.Mcp;

/// <summary>
/// Talks to the in-Revit bridge (McpBridgeServer) over plain TCP at 127.0.0.1:&lt;port&gt;.
///
/// Connect-per-request: every call opens a fresh TCP connection, sends one JSON request, reads what
/// comes back, closes. No persistent socket, no reconnect logic. This is the robust, dead-simple
/// model used by mcp-servers-for-revit; it removes the stale-connection / handshake fragility we hit
/// with a long-lived WebSocket.
///
/// What comes back on an invoke is no longer exactly one JSON value: the bridge may send interim
/// progress frames ({ id, progress }) before the reply, on the same connection (#108). The reader
/// therefore splits the stream into frames and hands the progress ones to a sink. And a call can be
/// LEFT: after <see cref="HandleAfter"/> the client stops waiting, closes its connection and returns
/// the call's id as a job handle — the bridge keeps the command running and stores its outcome, which
/// <see cref="GetJobResultAsync"/> collects and <see cref="CancelJobAsync"/> stops (#99, #109, #110).
/// </summary>
internal sealed class RevitBridgeClient
{
    private const string Host = "127.0.0.1";

    /// <summary>
    /// Upper bound on ONE command invocation while this client waits for it. A blocked Revit — a modal
    /// dialog, an edit mode, a native command — cannot serve the request until it goes idle again, so an
    /// unbounded read waits forever and takes the agent's call with it. Deliberately generous; and since
    /// <see cref="HandleAfter"/>, rarely the bound that fires.
    /// </summary>
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromMinutes(10);

    private readonly int _port;
    private readonly string _token;

    public RevitBridgeClient(int port, string token, TimeSpan? handleAfter = null)
    {
        _port = port;
        _token = token;
        HandleAfter = handleAfter ?? TimeSpan.FromSeconds(40);
    }

    /// <summary>
    /// How long a call is waited for before it is handed back as a running job. Below the patience of
    /// the AI clients seen in the field: Claude Code drops a tool call at exactly 60 s (measured
    /// 2026-09-02 — a first version handed back at 60 s and lost the race by a second), and #99's client
    /// at four minutes. Zero or negative means "wait the whole InvokeTimeout".
    /// </summary>
    public TimeSpan HandleAfter { get; }

    /// <summary>The catalog stamp carried by the LAST reply from the bridge (null until one arrived).
    /// See McpWire.Catalog: the exe compares it with the stamp it listed under.</summary>
    public string? LastCatalog { get; private set; }

    /// <summary>Asks the bridge for its current catalog stamp and nothing else. Fast to fail: this is
    /// the poller's request, and an unreachable Revit must not tie it up.</summary>
    public async Task<string?> GetCatalogAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        JsonNode? result = await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeVersion }, timeout.Token);
        return result?[McpWire.Catalog]?.GetValue<string>();
    }

    public async Task<JsonNode?> ListCommandsAsync(CancellationToken ct)
    {
        // Discovery must never hang the whole MCP server: fail fast so tools/list completes (empty)
        // instead of letting the AI client time the server out (~30s) and mark it disconnected.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeList }, timeout.Token);
    }

    /// <summary>What an invoke came back with: the result, or the id of the job still running.</summary>
    public sealed record InvokeOutcome(JsonNode? Result, string? RunningJobId);

    /// <summary>
    /// Invokes a command. Progress frames go to <paramref name="progress"/> as (fraction, message).
    /// When the caller's token is cancelled mid-call, a cancel is sent to the bridge for the same id
    /// before the cancellation propagates — the command stops, not just the wait for it.
    /// </summary>
    public async Task<InvokeOutcome> InvokeAsync(string command, JsonNode? payload, IProgress<(double Fraction, string? Message)>? progress, CancellationToken ct)
    {
        string id = NewId();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InvokeTimeout);
        using var handleTimer = new CancellationTokenSource();
        if (HandleAfter > TimeSpan.Zero) handleTimer.CancelAfter(HandleAfter);
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, handleTimer.Token);

        JsonObject envelope = new()
        {
            [McpWire.Type] = McpWire.TypeInvoke,
            [McpWire.Command] = command,
            [McpWire.Payload] = payload,
        };
        try
        {
            JsonNode? result = await SendAsync(envelope, waitCts.Token, id, progress);
            return new InvokeOutcome(result, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller's cancellation is theirs and must propagate as one — but first it has to
            // reach Revit, or the command runs to completion for nobody.
            await CancelJobQuietlyAsync(id);
            throw;
        }
        catch (OperationCanceledException) when (handleTimer.IsCancellationRequested)
        {
            // Not an error: the command is running, and the caller gets a handle instead of a wait.
            // The connection is closed by now; the bridge keeps the job and its outcome.
            return new InvokeOutcome(null, id);
        }
        catch (OperationCanceledException)
        {
            throw new BridgeException(McpWire.Codes.Timeout,
                $"'{command}' did not answer within {InvokeTimeout.TotalMinutes:0} minutes. Revit is most " +
                "likely blocked by a modal dialog or an edit mode.",
                $"The command may still finish: call GetJobResult with jobId \"{id}\" later, and GetQueueStatus " +
                "to see whether Revit is waiting for the user.");
        }
    }

    /// <summary>The stored outcome of a call, by the id InvokeAsync handed back (or that the timeout hint named).</summary>
    public async Task<JsonNode?> GetJobResultAsync(string jobId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeResult }, timeout.Token, jobId);
    }

    /// <summary>The recent calls, newest first — for a caller whose handle never arrived.</summary>
    public async Task<JsonNode?> ListJobsAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeResult, [McpWire.List] = true }, timeout.Token);
    }

    /// <summary>Asks the bridge to cancel a running call. True when it was running and has been told.</summary>
    public async Task<bool> CancelJobAsync(string jobId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        JsonNode? result = await SendAsync(new JsonObject { [McpWire.Type] = McpWire.TypeCancel }, timeout.Token, jobId);
        return result?[McpWire.Cancelled]?.GetValue<bool>() ?? false;
    }

    private async Task CancelJobQuietlyAsync(string jobId)
    {
        try { await CancelJobAsync(jobId, CancellationToken.None); }
        catch { /* best effort: Revit may be gone, and the caller is leaving anyway */ }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Replies carry whole query results, so the exe accepts far more than the bridge does
    /// for a request.</summary>
    private const int MaxReplyBytes = 64 * 1024 * 1024;

    private async Task<JsonNode?> SendAsync(JsonObject envelope, CancellationToken ct, string? id = null, IProgress<(double, string?)>? progress = null)
    {
        envelope[McpWire.Id] = id ?? NewId();
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

        JsonFrameReader frames = new(stream, MaxReplyBytes);
        while (true)
        {
            string frameText = await frames.ReadFrameAsync(ct)
                ?? throw new InvalidOperationException("Revit closed the connection without a response.");
            JsonNode? node = JsonNode.Parse(frameText);

            // An interim frame: report and keep reading. Never the reply, whatever else it carries.
            if (node?[McpWire.Progress] is JsonObject interim)
            {
                progress?.Report((
                    interim[McpWire.ProgressFraction]?.GetValue<double>() ?? 0,
                    interim[McpWire.ProgressMessage]?.GetValue<string>()));
                continue;
            }

            // Taken from every reply, error or not: the stamp says what the command set IS, and a failed
            // call is as good a messenger as a successful one.
            if (node?[McpWire.Catalog]?.GetValue<string>() is { Length: > 0 } stamp)
                LastCatalog = stamp;

            if (node?[McpWire.Error] is JsonNode err)
                throw ToException(err);

            return node?[McpWire.Result]?.DeepClone();
        }
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
}
