using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Utils;
using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Runtime.CompilerServices;

namespace AnalyseTool.Tests;

/// <summary>Collects log events in memory, so a test can read what a decorator wrote.</summary>
internal sealed class CapturingSink : ILogEventSink
{
    private readonly List<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events { get { lock (_events) return _events.ToList(); } }

    public void Emit(LogEvent logEvent) { lock (_events) _events.Add(logEvent); }

    public ILogger CreateLogger() =>
        new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

    /// <summary>The events at <paramref name="level"/>, rendered, for Contains-style assertions.</summary>
    public List<string> At(LogEventLevel level) =>
        Events.Where(e => e.Level == level).Select(e => e.RenderMessage()).ToList();
}

/// <summary>The command pipeline's stages, each against a fake inner stage — no dispatcher, no Revit.</summary>
public class CommandPipelineTests
{
    private sealed class FakeExecutor : ICommandExecutor
    {
        private readonly Func<CommandRequest, Task<object?>> _run;
        public FakeExecutor(Func<CommandRequest, Task<object?>> run) => _run = run;
        public CommandRequest? LastRequest { get; private set; }
        public int Calls { get; private set; }

        public Task<object?> ExecuteAsync(CommandRequest request)
        {
            Calls++;
            LastRequest = request;
            return _run(request);
        }
    }

    private static CommandRequest Request(string command = "GetElements", JToken? payload = null) =>
        new(command, payload ?? JValue.CreateNull(), "test");

    [Test]
    public async Task A_finished_command_is_logged_with_its_duration()
    {
        CapturingSink sink = new();
        LoggingExecutor logging = new(new FakeExecutor(_ => Task.FromResult<object?>(42)), sink.CreateLogger());

        object? result = await logging.ExecuteAsync(Request());

        await Assert.That(result).IsEqualTo(42);
        List<string> info = sink.At(LogEventLevel.Information);
        await Assert.That(info.Count).IsEqualTo(1);
        await Assert.That(info[0]).Contains("GetElements");
        await Assert.That(info[0]).Contains("finished in");
    }

    [Test]
    public async Task A_failed_command_is_logged_with_its_root_cause_and_payload()
    {
        // The Revit-thread marshalling wraps the exception; the log must name what actually broke.
        CapturingSink sink = new();
        LoggingExecutor logging = new(new FakeExecutor(_ =>
                throw new AggregateException(new InvalidOperationException("Category 'Wände' not found"))),
            sink.CreateLogger());

        await Assert.That(() => logging.ExecuteAsync(Request(payload: JObject.Parse("""{"category":"Wände"}"""))))
            .Throws<AggregateException>();

        LogEvent error = sink.Events.Single(e => e.Level == LogEventLevel.Error);
        await Assert.That(error.Exception).IsTypeOf<AggregateException>();
        string text = error.RenderMessage();
        await Assert.That(text).Contains("InvalidOperationException");
        await Assert.That(text).Contains("Category 'Wände' not found");
        await Assert.That(text).Contains("category");
    }

    [Test]
    public async Task A_cancelled_command_is_information_not_an_error()
    {
        CapturingSink sink = new();
        LoggingExecutor logging = new(new FakeExecutor(_ => throw new OperationCanceledException()), sink.CreateLogger());

        await Assert.That(() => logging.ExecuteAsync(Request())).Throws<OperationCanceledException>();

        await Assert.That(sink.At(LogEventLevel.Error).Count).IsEqualTo(0);
        await Assert.That(sink.At(LogEventLevel.Information).Single()).Contains("cancelled");
    }

    [Test]
    public async Task A_status_poll_leaves_no_trace_in_the_log()
    {
        CapturingSink sink = new();
        LoggingExecutor logging = new(new FakeExecutor(_ => Task.FromResult<object?>(null)), sink.CreateLogger());

        await logging.ExecuteAsync(Request("GetQueueStatus"));

        await Assert.That(sink.Events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_gate_that_says_no_stops_the_command_before_it_runs()
    {
        FakeExecutor inner = new(_ => Task.FromResult<object?>(null));
        CommandRegistration registration = new("SetCodeExecution", typeof(object), "core");
        GatingExecutor gating = new(inner, name => name == registration.Name ? registration : null);

        CommandRequest refused = Request("SetCodeExecution") with { Gate = _ => Task.FromResult(false) };
        await Assert.That(() => gating.ExecuteAsync(refused)).Throws<UnauthorizedAccessException>();
        await Assert.That(inner.Calls).IsEqualTo(0);

        // An unknown name has nothing to authorize: the dispatcher refuses it by name further in.
        CommandRequest unknown = Request("NoSuchCommand") with { Gate = _ => Task.FromResult(false) };
        await gating.ExecuteAsync(unknown);
        await Assert.That(inner.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task A_running_command_is_listed_and_can_be_cancelled_by_run_id()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeExecutor inner = new(async request =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, request.CancellationToken);
            return null;
        });
        TrackingExecutor tracking = new(inner);

        Task<object?> run = tracking.ExecuteAsync(Request());
        await started.Task;

        RunningCommand running = tracking.Running.Single();
        await Assert.That(running.Command).IsEqualTo("GetElements");
        await Assert.That(tracking.TryCancel(running.Id)).IsTrue();
        await Assert.That(() => run).Throws<OperationCanceledException>();
        await Assert.That(tracking.Running.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Progress_reaches_both_the_transport_sink_and_the_listeners()
    {
        List<ProgressInfo> transport = new();
        List<ProgressInfo> listeners = new();
        FakeExecutor inner = new(request =>
        {
            request.Progress!.Report(new ProgressInfo(0.5, "half"));
            return Task.FromResult<object?>(null);
        });
        TrackingExecutor tracking = new(inner);
        tracking.ProgressReported += (_, info) => listeners.Add(info);

        await tracking.ExecuteAsync(Request() with { Progress = new ListProgress(transport) });

        await Assert.That(transport.Count).IsEqualTo(1);
        await Assert.That(listeners.Count).IsEqualTo(1);
    }

    private sealed class ListProgress : IProgress<ProgressInfo>
    {
        private readonly List<ProgressInfo> _list;
        public ListProgress(List<ProgressInfo> list) => _list = list;
        public void Report(ProgressInfo value) => _list.Add(value);
    }
}

/// <summary>The AI client decorators, against a fake IChatClient — no network.</summary>
public class ChatClientDecoratorTests
{
    /// <summary>Streams "Hello world" in two parts, or never answers when <c>hang</c> is set.</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly bool _hang;
        public FakeChatClient(bool hang = false) => _hang = hang;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (_hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello world"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello ");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "world");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static readonly List<ChatMessage> Prompt = new() { new ChatMessage(ChatRole.User, "hi") };

    private static async Task<string?> Drain(IChatClient client, CancellationToken ct = default)
    {
        string text = string.Empty;
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(Prompt, null, ct))
            text += update.Text;
        return text;
    }

    [Test]
    public async Task A_streamed_answer_passes_through_and_is_logged_once_with_its_length()
    {
        CapturingSink sink = new();
        IChatClient client = new SerilogChatClient(
            new TimeoutChatClient(new FakeChatClient(), TimeSpan.FromSeconds(30)), "Ollama", "llama3", sink.CreateLogger());

        string? text = await Drain(client);

        await Assert.That(text).IsEqualTo("Hello world");
        string line = sink.At(LogEventLevel.Information).Single();
        await Assert.That(line).Contains("AI call ok");
        await Assert.That(line).Contains("11");
    }

    [Test]
    public async Task An_endpoint_that_never_answers_is_logged_as_a_timeout()
    {
        CapturingSink sink = new();
        IChatClient client = new SerilogChatClient(
            new TimeoutChatClient(new FakeChatClient(hang: true), TimeSpan.FromMilliseconds(50)), "Ollama", "llama3", sink.CreateLogger());

        using CancellationTokenSource caller = new();
        await Assert.That(() => Drain(client, caller.Token)).Throws<OperationCanceledException>();

        // The commands tell a timeout from a cancel by whose token fired — the caller's must not have.
        await Assert.That(caller.IsCancellationRequested).IsFalse();
        await Assert.That(sink.At(LogEventLevel.Warning).Single()).Contains("timed out");
    }

    [Test]
    public async Task A_caller_who_cancels_is_not_logged_as_a_timeout()
    {
        CapturingSink sink = new();
        IChatClient client = new SerilogChatClient(
            new TimeoutChatClient(new FakeChatClient(hang: true), TimeSpan.FromSeconds(30)), "Ollama", "llama3", sink.CreateLogger());

        using CancellationTokenSource caller = new(TimeSpan.FromMilliseconds(50));
        await Assert.That(() => Drain(client, caller.Token)).Throws<OperationCanceledException>();

        await Assert.That(sink.At(LogEventLevel.Warning).Count).IsEqualTo(0);
        await Assert.That(sink.At(LogEventLevel.Information).Single()).Contains("cancelled by the caller");
    }
}

public class AsyncTtlCacheTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Test]
    public async Task A_value_is_fetched_once_until_it_expires()
    {
        ManualTime time = new();
        AsyncTtlCache<string> cache = new(TimeSpan.FromMinutes(15), time);
        int fetches = 0;
        Task<string?> Fetch(CancellationToken _) { fetches++; return Task.FromResult<string?>($"v{fetches}"); }

        await Assert.That(await cache.GetOrCreateAsync(Fetch, default)).IsEqualTo("v1");
        time.Now += TimeSpan.FromMinutes(14);
        await Assert.That(await cache.GetOrCreateAsync(Fetch, default)).IsEqualTo("v1");
        time.Now += TimeSpan.FromMinutes(2);
        await Assert.That(await cache.GetOrCreateAsync(Fetch, default)).IsEqualTo("v2");
        await Assert.That(fetches).IsEqualTo(2);
    }

    [Test]
    public async Task A_failed_fetch_is_not_cached()
    {
        AsyncTtlCache<string> cache = new(TimeSpan.FromMinutes(15), new ManualTime());
        int fetches = 0;
        Task<string?> Fetch(CancellationToken _) { fetches++; return Task.FromResult<string?>(fetches == 1 ? null : "ok"); }

        await Assert.That(await cache.GetOrCreateAsync(Fetch, default)).IsNull();
        await Assert.That(await cache.GetOrCreateAsync(Fetch, default)).IsEqualTo("ok");
    }

    [Test]
    public async Task Concurrent_callers_share_one_fetch()
    {
        AsyncTtlCache<string> cache = new(TimeSpan.FromMinutes(15), new ManualTime());
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetches = 0;
        Task<string?> Fetch(CancellationToken _) { Interlocked.Increment(ref fetches); return answer.Task; }

        Task<string?> first = cache.GetOrCreateAsync(Fetch, default);
        Task<string?> second = cache.GetOrCreateAsync(Fetch, default);
        answer.SetResult("v1");

        await Assert.That(await first).IsEqualTo("v1");
        await Assert.That(await second).IsEqualTo("v1");
        await Assert.That(fetches).IsEqualTo(1);
    }
}
