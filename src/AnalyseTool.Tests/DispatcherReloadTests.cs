using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tests;

/// <summary>A reload swaps the extension commands in one step. The registry used to be cleared and then
/// refilled while sources compiled, and every call in between was refused as "not registered".</summary>
public class DispatcherReloadTests
{
    /// <summary>A command type for the dispatcher to register from this assembly; never executed.</summary>
    [RevitCommand(Description = "Probe for the dispatcher tests.")]
    public sealed class Probe : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) => Task.FromResult<object?>(null);
    }

    // The hub is only reached when a command executes, which these tests never do.
    private static CommandDispatcher NewDispatcher() => new(null!);

    [Test]
    public async Task The_old_commands_stay_callable_until_the_new_set_replaces_them()
    {
        CommandDispatcher dispatcher = NewDispatcher();
        ExtensionRegistrations first = dispatcher.NewExtensionSet();
        dispatcher.RegisterExtension(typeof(Probe).Assembly, "acme", first);
        dispatcher.ReplaceExtensions(first);

        // A reload in progress: the next set is being filled, and is empty so far.
        ExtensionRegistrations next = dispatcher.NewExtensionSet();
        await Assert.That(dispatcher.IsRegistered("acme.Probe")).IsTrue();

        dispatcher.ReplaceExtensions(next);
        await Assert.That(dispatcher.IsRegistered("acme.Probe")).IsFalse();
    }

    [Test]
    public async Task A_snapshot_taken_before_a_reload_is_not_changed_by_it()
    {
        CommandDispatcher dispatcher = NewDispatcher();
        ExtensionRegistrations set = dispatcher.NewExtensionSet();
        dispatcher.RegisterExtension(typeof(Probe).Assembly, "acme", set);
        dispatcher.ReplaceExtensions(set);

        IReadOnlyCollection<CommandRegistration> before = dispatcher.RegisteredCommands;
        dispatcher.ReplaceExtensions(dispatcher.NewExtensionSet());

        await Assert.That(before.Select(c => c.Name)).Contains("acme.Probe");
    }
}
