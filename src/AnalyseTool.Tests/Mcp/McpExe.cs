using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests.Mcp;

/// <summary>
/// The published MCP exe, driven over stdio the way an AI client drives it: newline-delimited
/// JSON-RPC in, newline-delimited JSON-RPC out. Runs the exe from its build output next to this
/// project (no Revit, no deploy), so what is tested is exactly what would ship.
/// </summary>
internal sealed class McpExe : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Channel<JObject> _messages = Channel.CreateUnbounded<JObject>();
    private readonly StringBuilder _stderr = new();
    private int _nextId;

    private McpExe(Process process) => _process = process;

    public string Stderr { get { lock (_stderr) return _stderr.ToString(); } }

    public static McpExe Start(int bridgePort, string token = "test-token", params string[] extraArgs)
    {
        // <src>/AnalyseTool.Tests/bin/<cfg>/net8.0-windows/  ->  <src>/AnalyseTool.Mcp/bin/<cfg>/net8.0/AnalyseTool.Mcp.dll
        string testBin = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string cfg = Path.GetFileName(Path.GetDirectoryName(testBin)!);
        string src = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", "..")); // net8.0-windows -> cfg -> bin -> AnalyseTool.Tests -> src
        string dll = Path.Combine(src, "AnalyseTool.Mcp", "bin", cfg, "net8.0", "AnalyseTool.Mcp.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException("Build AnalyseTool.Mcp first (the test project references it for build order).", dll);

        ProcessStartInfo psi = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(bridgePort.ToString());
        psi.ArgumentList.Add("--token"); psi.ArgumentList.Add(token);
        foreach (string extra in extraArgs) psi.ArgumentList.Add(extra);

        Process process = Process.Start(psi) ?? throw new InvalidOperationException("dotnet did not start");
        McpExe exe = new(process);
        _ = Task.Run(exe.PumpStdoutAsync);
        _ = Task.Run(exe.PumpStderrAsync);
        return exe;
    }

    private async Task PumpStdoutAsync()
    {
        while (await _process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { _messages.Writer.TryWrite(JObject.Parse(line)); }
            catch { /* not JSON-RPC: ignore */ }
        }
        _messages.Writer.TryComplete();
    }

    private async Task PumpStderrAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is { } line)
            lock (_stderr) _stderr.AppendLine(line);
    }

    /// <summary>initialize + initialized: the handshake every client does before anything else.</summary>
    public async Task InitializeAsync()
    {
        await RequestAsync("initialize", new JObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JObject(),
            ["clientInfo"] = new JObject { ["name"] = "AnalyseTool.Tests", ["version"] = "0" },
        });
        await NotifyAsync("notifications/initialized");
    }

    public async Task<JObject> RequestAsync(string method, JObject? @params = null, TimeSpan? timeout = null)
    {
        int id = Interlocked.Increment(ref _nextId);
        JObject msg = new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null) msg["params"] = @params;
        await SendAsync(msg);

        using CancellationTokenSource cts = new(timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            JObject reply = await ReadAsync(cts.Token);
            if ((int?)reply["id"] == id)
            {
                if (reply["error"] is JObject err) throw new InvalidOperationException($"{method}: {err}");
                return (JObject?)reply["result"] ?? new JObject();
            }
            // Notifications interleaved with the reply are kept for whoever waits on them.
            if (reply["method"] is not null) _sideNotifications.Enqueue(reply);
        }
    }

    public Task NotifyAsync(string method, JObject? @params = null)
    {
        JObject msg = new() { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null) msg["params"] = @params;
        return SendAsync(msg);
    }

    /// <summary>Sends a request WITHOUT waiting — for a test that wants to cancel it, or to watch
    /// what happens while it is in flight. Pair with <see cref="WaitForReplyAsync"/>.</summary>
    public async Task<int> BeginRequestAsync(string method, JObject? @params = null)
    {
        int id = Interlocked.Increment(ref _nextId);
        JObject msg = new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null) msg["params"] = @params;
        await SendAsync(msg);
        return id;
    }

    /// <summary>The whole reply (result or error) for a request begun earlier; null when the timeout
    /// passes without one — which after a cancellation is a legitimate outcome.</summary>
    public async Task<JObject?> WaitForReplyAsync(int id, TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        try
        {
            while (true)
            {
                JObject reply = await ReadAsync(cts.Token);
                if ((int?)reply["id"] == id) return reply;
                if (reply["method"] is not null) _sideNotifications.Enqueue(reply);
            }
        }
        catch (OperationCanceledException) { return null; }
    }

    private readonly Queue<JObject> _sideNotifications = new();

    /// <summary>Waits for a notification with the given method, from the ones already seen or the
    /// stream; null when the timeout passes without one.</summary>
    public async Task<JObject?> WaitForNotificationAsync(string method, TimeSpan timeout)
    {
        while (_sideNotifications.TryDequeue(out JObject? queued))
            if ((string?)queued["method"] == method) return queued;

        using CancellationTokenSource cts = new(timeout);
        try
        {
            while (true)
            {
                JObject msg = await ReadAsync(cts.Token);
                if ((string?)msg["method"] == method) return msg;
            }
        }
        catch (OperationCanceledException) { return null; }
    }

    private async Task<JObject> ReadAsync(CancellationToken ct) => await _messages.Reader.ReadAsync(ct);

    private async Task SendAsync(JObject msg)
    {
        await _process.StandardInput.WriteLineAsync(msg.ToString(Newtonsoft.Json.Formatting.None));
        await _process.StandardInput.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { _process.StandardInput.Close(); } catch { }
        try { if (!_process.WaitForExit(3000)) _process.Kill(entireProcessTree: true); } catch { }
        _process.Dispose();
        await Task.CompletedTask;
    }
}
