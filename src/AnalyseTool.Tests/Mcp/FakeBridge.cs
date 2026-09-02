using System.Collections.Concurrent;
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
/// driven end to end — tools/list, tools/call, errors, the catalog stamp, progress frames, cancel and
/// stored results — with no Revit anywhere.
///
/// One connection per request, exactly like the real thing: the exe opens a socket, writes one JSON
/// value, reads what comes back, closes. Every reply carries the current <see cref="Stamp"/>, so a
/// test can move it and watch the exe notice. An invoke is a JOB here too: it runs on when the exe
/// closes the socket early, and a "result" request on a later connection finds its outcome.
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
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private Task? _loop;

    public int Port { get; private set; }

    /// <summary>Changes on reload / C# switch in the real bridge; a test moves it by hand.</summary>
    public volatile string Stamp = "0:1";

    public List<CannedCommand> Commands { get; } = new();

    /// <summary>What an invoke answers: a result, or an error object { code, message, hint }.</summary>
    public Func<string, JToken?, (JToken? Result, JObject? Error)> OnInvoke { get; set; } =
        (_, _) => (new JObject { ["ok"] = true }, null);

    /// <summary>The long form, for tests about progress and cancellation: gets a progress sink and the
    /// job's token, and may take its time. When set, it wins over <see cref="OnInvoke"/>.</summary>
    public Func<string, JToken?, IProgress<(double Fraction, string? Message)>, CancellationToken, Task<(JToken? Result, JObject? Error)>>? OnInvokeAsync { get; set; }

    public List<(string Command, JToken? Payload)> Invocations { get; } = new();

    /// <summary>Ids the exe asked to cancel, in order.</summary>
    public List<string> Cancellations { get; } = new();

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
            SemaphoreSlim writeLock = new(1, 1);

            async Task WriteAsync(JObject frame)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(frame.ToString(Formatting.None));
                await writeLock.WaitAsync();
                try
                {
                    await stream.WriteAsync(bytes);
                    await stream.FlushAsync();
                }
                finally { writeLock.Release(); }
            }

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
            else if (type == McpWire.TypeCancel)
            {
                lock (Cancellations) Cancellations.Add(id ?? string.Empty);
                bool cancelled = id is not null && _jobs.TryGetValue(id, out Job? job) && job.Finished is null;
                if (cancelled) _jobs[id!].Cancellation.Cancel();
                reply[McpWire.Result] = new JObject { [McpWire.Cancelled] = cancelled };
            }
            else if (type == McpWire.TypeResult)
            {
                reply[McpWire.Result] = id is not null && _jobs.TryGetValue(id, out Job? job)
                    ? job.Describe()
                    : new JObject { [McpWire.JobStatus] = McpWire.JobStates.Unknown };
            }
            else
            {
                string command = (string?)req[McpWire.Command] ?? string.Empty;
                JToken? payload = req[McpWire.Payload];
                lock (Invocations) Invocations.Add((command, payload));

                Job job = new(command);
                if (id is not null) _jobs[id] = job;
                Progress<(double, string?)> progress = new(p =>
                    _ = SafeWriteAsync(WriteAsync, new JObject
                    {
                        [McpWire.Id] = id,
                        [McpWire.Progress] = new JObject
                        {
                            [McpWire.ProgressFraction] = p.Item1,
                            [McpWire.ProgressMessage] = p.Item2,
                        },
                    }));

                (JToken? result, JObject? error) = OnInvokeAsync is not null
                    ? await OnInvokeAsync(command, payload, progress, job.Cancellation.Token)
                    : OnInvoke(command, payload);
                if (job.Cancellation.IsCancellationRequested && error is null)
                    error = new JObject { [McpWire.ErrorCode] = McpWire.Codes.Cancelled, [McpWire.ErrorMessage] = "The call was cancelled before it finished." };
                job.Finish(result, error);
                if (error is not null) reply[McpWire.Error] = error;
                else reply[McpWire.Result] = result ?? JValue.CreateNull();
            }
            reply[McpWire.Catalog] = Stamp;
            await SafeWriteAsync(WriteAsync, reply);
        }
    }

    private static async Task SafeWriteAsync(Func<JObject, Task> write, JObject frame)
    {
        try { await write(frame); }
        catch { /* the exe closed the socket — the job goes on, exactly like the real bridge */ }
    }

    private sealed class Job
    {
        public Job(string command) => Command = command;
        public string Command { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public DateTime Started { get; } = DateTime.UtcNow;
        public DateTime? Finished { get; private set; }
        public JToken? Result { get; private set; }
        public JObject? Error { get; private set; }

        public void Finish(JToken? result, JObject? error)
        {
            Result = result;
            Error = error;
            Finished = DateTime.UtcNow;
        }

        public JObject Describe()
        {
            string status = Finished is null ? McpWire.JobStates.Running
                : Error is null ? McpWire.JobStates.Done
                : (string?)Error[McpWire.ErrorCode] == McpWire.Codes.Cancelled ? McpWire.JobStates.Cancelled
                : McpWire.JobStates.Failed;
            JObject answer = new()
            {
                [McpWire.JobStatus] = status,
                [McpWire.JobCommand] = Command,
                [McpWire.JobSeconds] = Math.Round(((Finished ?? DateTime.UtcNow) - Started).TotalSeconds, 1),
            };
            if (Result is not null) answer[McpWire.JobResult] = Result;
            if (Error is not null) answer[McpWire.JobError] = Error;
            return answer;
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
