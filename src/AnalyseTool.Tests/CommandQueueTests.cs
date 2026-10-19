using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests;

/// <summary>The queue — the one entry point every transport uses — through the real pipeline and
/// dispatcher, up to the point a command would run. Running one needs a RevitContext, whose signatures
/// need RevitAPIUI: that part belongs to the in-Revit tier.</summary>
public class CommandQueueTests
{
    [RevitCommand(Description = "A command for the queue to find; never run here.")]
    public sealed class Echo : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) => Task.FromResult<object?>(null);
    }

    private static CommandQueue NewQueue()
    {
        CommandDispatcher dispatcher = new(null!);
        ExtensionRegistrations set = dispatcher.NewExtensionSet();
        dispatcher.RegisterExtension(typeof(Echo).Assembly, "test", set);
        dispatcher.ReplaceExtensions(set);
        return new CommandQueue(dispatcher);
    }

    [Test]
    public async Task An_unknown_command_is_refused_by_name()
    {
        await Assert.That(async () => await NewQueue().ExecuteAsync(new CommandRequest("test.Nope", JValue.CreateNull(), "test")))
            .Throws<InvalidOperationException>().WithMessageContaining("test.Nope");
    }

    [Test]
    public async Task A_gate_that_says_no_stops_the_command_and_nothing_stays_running()
    {
        CommandQueue queue = NewQueue();
        CommandRequest request = new("test.Echo", JValue.CreateNull(), "test") { Gate = _ => Task.FromResult(false) };

        await Assert.That(async () => await queue.ExecuteAsync(request)).Throws<UnauthorizedAccessException>();
        await Assert.That(queue.Running).IsEmpty();
    }

    [Test]
    public async Task The_gate_sees_the_command_it_decides_on()
    {
        CommandRegistration? seen = null;
        CommandRequest request = new("test.Echo", JValue.CreateNull(), "test")
        {
            Gate = registration => { seen = registration; return Task.FromResult(false); },
        };

        try { await NewQueue().ExecuteAsync(request); } catch (UnauthorizedAccessException) { }

        await Assert.That(seen?.Name).IsEqualTo("test.Echo");
    }
}
