using AnalyseTool.Hosting.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.Hosting.Tests;

public sealed class CommandRegistrationTests
{
    [Test]
    public async Task GetName_ClassWithCommandSuffix_DropsTheSuffix()
    {
        await Assert.That(CommandNaming.GetName(typeof(GreetCommand))).IsEqualTo("Greet");
    }

    [Test]
    public async Task GetName_ClassWithoutSuffix_IsTheClassName()
    {
        await Assert.That(CommandNaming.GetName(typeof(DoNothing))).IsEqualTo("DoNothing");
    }

    [Test]
    public async Task GetName_NameInAttribute_OverridesTheClassName()
    {
        await Assert.That(CommandNaming.GetName(typeof(OldNameCommand))).IsEqualTo("Renamed");
    }

    [Test]
    public async Task Scan_ValidCommands_DescribesTypesAndDescription()
    {
        var (commands, errors) = CommandScanner.Scan([typeof(GreetCommand), typeof(DoNothing), typeof(Recorder)], _ => false);

        var greet = commands.Single(command => command.Name == "Greet");
        using (Assert.Multiple())
        {
            await Assert.That(errors).IsEmpty();
            await Assert.That(commands.Count).IsEqualTo(2);
            await Assert.That(greet.RequestType).IsEqualTo(typeof(GreetRequest));
            await Assert.That(greet.ResponseType).IsEqualTo(typeof(string));
            await Assert.That(greet.Description).IsEqualTo("Greets by name.");
        }
    }

    [Test]
    public async Task Scan_SameNameTwice_NamesBothClasses()
    {
        var (_, errors) = CommandScanner.Scan([typeof(DuplicateA), typeof(DuplicateB)], _ => false);

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0]).Contains(typeof(DuplicateA).FullName!).And.Contains(typeof(DuplicateB).FullName!);
    }

    [Test]
    public async Task Scan_NamesDifferInCaseOnly_IsDuplicate()
    {
        var (_, errors) = CommandScanner.Scan([typeof(GreetCommand), typeof(LowerCaseGreet)], _ => false);

        await Assert.That(errors).HasSingleItem();
    }

    [Test]
    public async Task Scan_DotInApplicationCommandName_IsError()
    {
        var (_, errors) = CommandScanner.Scan([typeof(DottedName)], _ => false);

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0]).Contains("dot");
    }

    [Test]
    public async Task Scan_DotInBuiltInCommandName_IsAllowed()
    {
        var (commands, errors) = CommandScanner.Scan([typeof(DottedName)], _ => true);

        await Assert.That(errors).IsEmpty();
        await Assert.That(commands.Single().Name).IsEqualTo("My.Command");
    }

    [Test]
    public async Task Scan_AbstractRequest_IsError()
    {
        var (_, errors) = CommandScanner.Scan([typeof(AbstractRequestCommand)], _ => false);

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0]).Contains(nameof(AbstractRequest));
    }

    [Test]
    public async Task Scan_TwoCommandInterfaces_IsError()
    {
        var (_, errors) = CommandScanner.Scan([typeof(TwoInterfaces)], _ => false);

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0]).Contains("more than once");
    }

    [Test]
    public async Task AddRevitCommands_BrokenCommands_ReportsEveryProblemAtOnce()
    {
        var builder = TestHost.CreateBuilder();

        var exception = Assert.Throws<CommandRegistrationException>(() =>
            builder.AddRevitCommands(commands => commands.FromTypes(typeof(DuplicateA), typeof(DuplicateB), typeof(DottedName), typeof(TwoInterfaces))));

        await Assert.That(exception.Errors.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AddRevitCommands_WithoutAddRevitHosting_ThrowsWithAHint()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddRevitCommands(commands => commands.FromTypes(typeof(GreetCommand))));

        await Assert.That(exception.Message).Contains("AddRevitHosting");
    }

    [Test]
    public async Task AddRevitCommands_RegistersCommandsAndBuiltIns()
    {
        using var host = TestHost.Build(commands => commands.FromTypes(typeof(GreetCommand), typeof(OldNameCommand)));

        var registry = host.Services.GetRequiredService<CommandRegistry>();
        var names = registry.Commands.Select(command => command.Name).Order().ToList();

        await Assert.That(names).IsEquivalentTo(["Greet", "Host.GetCommands", "Renamed"]);
        await Assert.That(registry.Find("greet")).IsNotNull();
        await Assert.That(registry.Find(typeof(OldNameCommand))!.Name).IsEqualTo("Renamed");
    }
}

[Command("greet")]
public sealed class LowerCaseGreet : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(0);
}
