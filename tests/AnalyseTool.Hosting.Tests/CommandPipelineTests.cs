using System.Text.Json;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Commands.BuiltIn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Tests;

public sealed class CommandPipelineTests
{
    [Test]
    public async Task Execute_CommandWithRequest_BindsThePayload()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand)));

        var result = await host.ExecuteAsync("greet", new { name = "Revit" });

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value).IsEqualTo("Hello, Revit");
    }

    [Test]
    public async Task Execute_EmptyResult_ReturnsNull()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(DoNothing)));

        var result = await host.ExecuteAsync("DoNothing");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task Execute_UnknownCommand_ReturnsNotFound()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand)));

        var result = await host.ExecuteAsync("Missing");

        await Assert.That(result.Error?.Code).IsEqualTo(CommandErrorCodes.NotFound);
    }

    [Test]
    public async Task Execute_MissingPayload_ReturnsBadRequest()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand)));

        var result = await host.ExecuteAsync("Greet");

        await Assert.That(result.Error?.Code).IsEqualTo(CommandErrorCodes.BadRequest);
    }

    [Test]
    public async Task Execute_MissingPayload_OptionalInputs_RunsWithTheDefaults()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(PageCommand)));

        var result = await host.ExecuteAsync("Page");

        await Assert.That(result.Value).IsEqualTo("10:none");
    }

    [Test]
    public async Task Execute_MissingPayload_StructWithARequiredInput_ReturnsBadRequest()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(OffsetCommand)));

        var result = await host.ExecuteAsync("Offset");

        await Assert.That(result.Error?.Code).IsEqualTo(CommandErrorCodes.BadRequest);
    }

    [Test]
    [Arguments(typeof(PageRequest), true)]
    [Arguments(typeof(GreetRequest), false)]
    [Arguments(typeof(OffsetRequest), false)]
    [Arguments(typeof(PlainSettings), true)]
    [Arguments(typeof(object), true)]
    public async Task AllowsEmptyPayload_FollowsTheConstructors(Type requestType, bool allowed)
    {
        await Assert.That(CommandScanner.AllowsEmptyPayload(requestType)).IsEqualTo(allowed);
    }

    [Test]
    public async Task Execute_PayloadOfWrongShape_ReturnsBadRequest()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand)));

        var result = await host.ExecuteAsync("Greet", new[] { 1, 2, 3 });

        await Assert.That(result.Error?.Code).IsEqualTo(CommandErrorCodes.BadRequest);
    }

    [Test]
    public async Task Execute_CommandException_ReturnsItsCodeAndMessage()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(RefuseCommand)));

        var result = await host.ExecuteAsync("Refuse");

        await Assert.That(result.Error).IsEqualTo(new CommandError("refused", "Not today."));
    }

    [Test]
    public async Task Execute_UnexpectedException_ReturnsFailed()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(CrashCommand)));

        var result = await host.ExecuteAsync("Crash");

        await Assert.That(result.Error).IsEqualTo(new CommandError(CommandErrorCodes.Failed, "Boom."));
    }

    [Test]
    public async Task Execute_FailureFromRibbon_NotifiesTheUser()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(RefuseCommand)));

        await host.ExecuteAsync("Refuse", source: CommandSources.Ribbon);

        await Assert.That(host.Services.GetRequiredService<FakeNotifier>().Errors).IsEquivalentTo(["Refuse: Not today."]);
    }

    [Test]
    public async Task Execute_FailureFromWebView_DoesNotNotify()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(RefuseCommand)));

        await host.ExecuteAsync("Refuse");

        await Assert.That(host.Services.GetRequiredService<FakeNotifier>().Errors).IsEmpty();
    }

    [Test]
    public async Task Execute_CancelledCall_ReturnsCanceled()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(ProbeCommand)));
        using var cancellation = new CancellationTokenSource();

        var call = host.ExecuteAsync(new CommandRequest("Probe", default, CommandSources.WebView) { CancellationToken = cancellation.Token });
        await host.Services.GetRequiredService<Recorder>().Ran.Task;
        await cancellation.CancelAsync();

        var result = await call;
        await Assert.That(result.Error?.Code).IsEqualTo(CommandErrorCodes.Canceled);
    }

    [Test]
    public async Task Execute_CancelledCall_LogsOnceWithoutWarning()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(ProbeCommand)).UseCommandLogging());
        using var cancellation = new CancellationTokenSource();

        var call = host.ExecuteAsync(new CommandRequest("Probe", default, CommandSources.WebView) { CancellationToken = cancellation.Token });
        await host.Services.GetRequiredService<Recorder>().Ran.Task;
        await cancellation.CancelAsync();
        await call;

        var entries = host.Services.GetRequiredService<LogRecorder>().Entries;
        await Assert.That(entries.Where(entry => entry.Level >= LogLevel.Warning)).IsEmpty();
        await Assert.That(entries.Count(entry => entry.Message.Contains("Probe (webview2) canceled by the caller"))).IsEqualTo(1);
    }

    [Test]
    public async Task Execute_Middleware_RunsInRegistrationOrderAndSeesHeaders()
    {
        using var host = TestHost.Build(commands => commands
            .FromTypes(typeof(ProbeCommand))
            .Use<FirstMiddleware>()
            .Use<SecondMiddleware>());

        var recorder = host.Services.GetRequiredService<Recorder>();
        recorder.Gate.SetResult();

        var request = new CommandRequest("Probe", default, CommandSources.WebView)
        {
            Headers = new Dictionary<string, string> { ["Document"] = "A.rvt" }
        };
        await host.ExecuteAsync(request);

        await Assert.That(string.Join(" → ", recorder.Entries)).IsEqualTo("first → second:A.rvt → command:webview2");
    }

    [Test]
    public async Task Execute_SingleFlight_RefusesASecondCallWhileTheFirstRuns()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(ProbeCommand)).UseSingleFlight());
        var recorder = host.Services.GetRequiredService<Recorder>();

        var first = host.ExecuteAsync("Probe");
        await recorder.Ran.Task;
        var second = await host.ExecuteAsync("Probe");
        recorder.Gate.SetResult();

        await Assert.That(second.Error?.Code).IsEqualTo(CommandErrorCodes.AlreadyRunning);
        await Assert.That((await first).Value).IsEqualTo("done");
        await Assert.That((await host.ExecuteAsync("Probe")).Succeeded).IsTrue();
    }

    [Test]
    public async Task Send_FromRibbon_RunsTheCommandWithoutWaiting()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(ProbeCommand)));
        var recorder = host.Services.GetRequiredService<Recorder>();

        host.Services.GetRequiredService<ICommandSender>().Send<ProbeCommand>();

        await Assert.That(await recorder.Ran.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo("command:ribbon");
        recorder.Gate.SetResult();
    }

    [Test]
    public async Task GetCommands_ListsCommandsWithDescriptionAndTypes()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand)));

        var result = await host.ExecuteAsync("Host.GetCommands");
        var commands = (IReadOnlyList<CommandInfo>)result.Value!;

        await Assert.That(commands).Contains(new CommandInfo("Greet", "Greets by name.", nameof(GreetRequest), nameof(String)));
    }

    [Test]
    public async Task Result_SerializesAsCamelCaseJson()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand)));

        var result = await host.ExecuteAsync("Host.GetCommands");
        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(json).Contains("\"name\":\"Greet\"");
    }
}
