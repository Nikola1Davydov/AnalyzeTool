using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Hosting.Tests;

/// <summary>Commands registered at run time: the commands of extensions come and go without a restart.</summary>
public sealed class CommandGroupTests
{
    private static CommandRegistry CreateRegistry()
    {
        var (commands, _) = CommandScanner.Scan([typeof(GreetCommand)], _ => false);
        return new CommandRegistry(commands, []);
    }

    private static CommandDescriptor Dynamic(string name)
    {
        return new CommandDescriptor(name, typeof(object), null, typeof(object), "A command of an extension.", (_, _, _) => Task.FromResult<object?>(name));
    }

    [Test]
    public async Task SetGroup_NewCommands_AreFoundAndGrowTheVersion()
    {
        var registry = CreateRegistry();
        var version = registry.Version;

        var problems = registry.SetGroup("acme", [Dynamic("Hello")]);

        using (Assert.Multiple())
        {
            await Assert.That(problems).IsEmpty();
            await Assert.That(registry.Find("hello")?.Group).IsEqualTo("acme");
            await Assert.That(registry.Version).IsGreaterThan(version);
            await Assert.That(registry.Commands.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task SetGroup_Again_ReplacesTheOldCommands()
    {
        var registry = CreateRegistry();
        registry.SetGroup("acme", [Dynamic("Hello"), Dynamic("Bye")]);

        registry.SetGroup("acme", [Dynamic("Hello")]);

        await Assert.That(registry.Find("Bye")).IsNull();
        await Assert.That(registry.Find("Hello")).IsNotNull();
    }

    [Test]
    public async Task SetGroup_NameOfABuiltInCommand_IsSkipped()
    {
        var registry = CreateRegistry();

        var problems = registry.SetGroup("acme", [Dynamic("Greet")]);

        await Assert.That(problems).HasSingleItem();
        await Assert.That(registry.Find("Greet")!.Group).IsNull();
    }

    [Test]
    public async Task SetGroup_NameTakenByAnotherGroup_FirstOneWins()
    {
        var registry = CreateRegistry();
        registry.SetGroup("first", [Dynamic("Hello")]);

        var problems = registry.SetGroup("second", [Dynamic("hello")]);

        await Assert.That(problems).HasSingleItem();
        await Assert.That(registry.Find("Hello")!.Group).IsEqualTo("first");
    }

    [Test]
    public async Task RemoveGroup_RaisesChangedAndForgetsTheCommands()
    {
        var registry = CreateRegistry();
        registry.SetGroup("acme", [Dynamic("Hello")]);
        var raised = 0;
        registry.Changed += (_, _) => raised++;

        registry.RemoveGroup("acme");

        await Assert.That(raised).IsEqualTo(1);
        await Assert.That(registry.Find("Hello")).IsNull();
        await Assert.That(registry.GetGroup("acme")).IsEmpty();
    }
}
