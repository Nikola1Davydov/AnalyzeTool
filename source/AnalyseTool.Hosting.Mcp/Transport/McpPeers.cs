using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AnalyseTool.Hosting.Mcp.Transport;

/// <summary>A Revit with the MCP server of this add-in, as its file in <c>mcp\</c> of the profile says.</summary>
/// <param name="Version">The Revit version, <c>2026</c>.</param>
/// <param name="Port">The port of its own endpoint.</param>
/// <param name="ProcessId">The Revit process.</param>
/// <param name="LastActive">When its window was in front last.</param>
public sealed record McpPeer(string Version, int Port, int ProcessId, DateTimeOffset LastActive);

/// <summary>
///     The Revit sessions that serve MCP on this machine — every Revit version, one shared address. Each server writes
///     <c>%LOCALAPPDATA%\&lt;add-in&gt;\mcp\&lt;process id&gt;.json</c> (its version, its port, when its window was in front) and
///     removes it when it stops. A tool call for another Revit is sent on to that Revit's own endpoint.
/// </summary>
internal sealed partial class McpPeers(McpHostSettings settings, ILogger<McpPeers> logger) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly ConcurrentDictionary<int, Task<McpClient>> _clients = new();
    private readonly int _processId = Environment.ProcessId;
    private Timer? _focus;
    private McpPeer? _self;
    private int _port;
    private DateTimeOffset _written;

    /// <summary>The Revit version of this process.</summary>
    public string Version => settings.RevitVersion;

    private string FilePath => Path.Combine(settings.PeersFolder, $"{_processId}-{_port}.json");

    /// <summary>Registers this Revit; its window being in front is noted every two seconds.</summary>
    public void Register(int port)
    {
        _port = port;
        _self = new McpPeer(settings.RevitVersion, port, _processId, DateTimeOffset.Now);
        Write(_self);
        _focus = new Timer(_ => NoteFocus(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    /// <summary>Removes the file of this Revit.</summary>
    public void Unregister()
    {
        _focus?.Dispose();
        _focus = null;
        _self = null;
        TryDelete(FilePath);
    }

    /// <summary>The Revit sessions alive now; the files of those that are gone are removed.</summary>
    public IReadOnlyList<McpPeer> List()
    {
        var result = new List<McpPeer>();
        if (!Directory.Exists(settings.PeersFolder))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(settings.PeersFolder, "*.json"))
        {
            McpPeer? peer;
            try
            {
                peer = JsonSerializer.Deserialize<McpPeer>(File.ReadAllText(file), Options);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                continue;
            }

            if (peer is null || !IsAlive(peer.ProcessId))
            {
                TryDelete(file);
                continue;
            }

            result.Add(peer);
        }

        return result.OrderByDescending(peer => peer.LastActive).ToList();
    }

    /// <summary>
    ///     The Revit a call goes to: the one of <paramref name="version" />, else the one used last. <see langword="null" /> when
    ///     the version is not open.
    /// </summary>
    public McpPeer? Resolve(string? version)
    {
        var peers = List();
        if (string.IsNullOrWhiteSpace(version))
        {
            return peers.FirstOrDefault() ?? _self;
        }

        return peers.FirstOrDefault(peer => IsSelf(peer) && peer.Version == version.Trim())
               ?? peers.FirstOrDefault(peer => peer.Version == version.Trim());
    }

    /// <summary>Whether a peer is this Revit.</summary>
    public bool IsSelf(McpPeer peer)
    {
        return peer.ProcessId == _processId && peer.Port == _port;
    }

    /// <summary>Calls a tool on another Revit, as its own MCP client.</summary>
    public async Task<CallToolResult> CallAsync(McpPeer peer, string tool, IReadOnlyDictionary<string, object?> arguments, string token, CancellationToken cancellationToken)
    {
        try
        {
            var client = await Connect(peer, token).ConfigureAwait(false);
            return await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A Revit that restarted has a new session: connect again once
            LogForwardRetry(logger, exception, peer.Version);
            if (_clients.TryRemove(peer.Port, out var stale))
            {
                await DisposeQuietly(stale).ConfigureAwait(false);
            }

            var client = await Connect(peer, token).ConfigureAwait(false);
            return await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Unregister();
        foreach (var client in _clients.Values)
        {
            await DisposeQuietly(client).ConfigureAwait(false);
        }

        _clients.Clear();
    }

    private Task<McpClient> Connect(McpPeer peer, string token)
    {
        return _clients.GetOrAdd(peer.Port, port =>
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://localhost:{port}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
            });
            return McpClient.CreateAsync(transport);
        });
    }

    private static async Task DisposeQuietly(Task<McpClient> client)
    {
        try
        {
            await (await client.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // It failed to connect, or the other Revit is gone
        }
    }

    private void NoteFocus()
    {
        if (_self is not { } self)
        {
            return;
        }

        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var owner) == 0 || owner != _processId)
            {
                return;
            }

            // In front: written at most every ten seconds, the file is only read when a call has no version
            var now = DateTimeOffset.Now;
            if (now - _written < TimeSpan.FromSeconds(10) && File.Exists(FilePath))
            {
                return;
            }

            _self = self with { LastActive = now };
            Write(_self);
        }
        catch (Exception exception)
        {
            LogFocusFailed(logger, exception);
        }
    }

    private void Write(McpPeer peer)
    {
        try
        {
            Directory.CreateDirectory(settings.PeersFolder);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(peer, Options));
            File.Move(temporary, FilePath, overwrite: true);
            _written = DateTimeOffset.Now;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFocusFailed(logger, exception);
        }
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [LoggerMessage(LogLevel.Debug, "MCP: could not note that this Revit is in front")]
    private static partial void LogFocusFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "MCP: the call to Revit {Version} failed, connecting again")]
    private static partial void LogForwardRetry(ILogger logger, Exception exception, string version);
}
