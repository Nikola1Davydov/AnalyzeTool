using System.Net;
using System.Net.Sockets;
using System.Text;
using AnalyseTool.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests.Mcp;

/// <summary>
/// The in-Revit half of the MCP transport, faked: a loopback listener that speaks the same envelope
/// (McpWire) the real bridge does and answers from a canned catalog. It lets the published exe be
/// driven end to end — tools/list, tools/call, errors, the catalog stamp — with no Revit anywhere.
///
/// One connection per request, exactly like the real thing: the exe opens a socket, writes one JSON
/// value, reads one JSON value, closes. Every reply carries the current <see cref="Stamp"/>, so a
/// test can move it and watch the exe notice.
/// </summary>
internal sealed class FakeBridge : IAsyncDisposable
{
    public sealed record CannedCommand(
        string Name,
        string Description,
        string InputSchema,
        string OutputSchema,
        bool ReadOnly = false,
        bool Destructive = false);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public int Port { get; private set; }

    /// <summary>Changes on reload / C# switch in the real bridge; a test moves it by hand.</summary>
    public volatile string Stamp = "0:1";

    public List<CannedCommand> Commands { get; } = new();

    /// <summary>What an invoke answers: a result, or an error object { code, message, hint }.</summary>
    public Func<string, JToken?, (JToken? Result, JObject? Error)> OnInvoke { get; set; } =
        (_, _) => (new JObject { ["ok"] = true }, null);

    public List<(string Command, JToken? Payload)> Invocations { get; } = new();

    public FakeBridge Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
        return this;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            string request = await ReadJsonAsync(stream);
            JObject req = JObject.Parse(request);
            string? id = (string?)req[McpWire.Id];
            string type = (string?)req[McpWire.Type] ?? McpWire.TypeInvoke;

            JObject reply = new() { [McpWire.Id] = id };
            if (type == McpWire.TypeVersion)
            {
                reply[McpWire.Result] = new JObject { [McpWire.Catalog] = Stamp };
            }
            else if (type == McpWire.TypeList)
            {
                reply[McpWire.Result] = new JObject
                {
                    [McpWire.Commands] = new JArray(Commands.Select(c => new JObject
                    {
                        [McpWire.Name] = c.Name,
                        [McpWire.SourceField] = "core",
                        [McpWire.Description] = c.Description,
                        [McpWire.ReadOnly] = c.ReadOnly,
                        [McpWire.Destructive] = c.Destructive,
                        [McpWire.InputSchema] = JToken.Parse(c.InputSchema),
                        [McpWire.OutputSchema] = JToken.Parse(c.OutputSchema),
                    })),
                };
            }
            else
            {
                string command = (string?)req[McpWire.Command] ?? string.Empty;
                JToken? payload = req[McpWire.Payload];
                lock (Invocations) Invocations.Add((command, payload));
                (JToken? result, JObject? error) = OnInvoke(command, payload);
                if (error is not null) reply[McpWire.Error] = error;
                else reply[McpWire.Result] = result ?? JValue.CreateNull();
            }
            reply[McpWire.Catalog] = Stamp;

            byte[] bytes = Encoding.UTF8.GetBytes(reply.ToString(Formatting.None));
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        }
    }

    private static async Task<string> ReadJsonAsync(NetworkStream stream)
    {
        using MemoryStream ms = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            ms.Write(buffer, 0, read);
            string text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            try { JToken.Parse(text); return text; }
            catch (JsonException) { /* incomplete, keep reading */ }
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_loop is not null) { try { await _loop; } catch { /* stopped */ } }
    }

    public const string ObjectSchema = """{"type":"object","properties":{"ok":{"type":"boolean"},"count":{"type":"integer"}},"required":["ok","count"]}""";
    public const string ArraySchema = """{"type":"array","items":{"type":"string"}}""";
    public const string EmptyInput = """{"type":"object","properties":{}}""";
    public const string LimitInput = """{"type":"object","properties":{"limit":{"type":["integer","null"]}}}""";
}
