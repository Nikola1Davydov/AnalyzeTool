using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Tests.Extensions;

/// <summary>A button from creation to a new version: C# sources compiled by AnalyseTool, loaded into the pipeline, versioned.</summary>
[NotInParallel("extensions")]
public sealed class ExtensionLifecycleTests
{
    [Test]
    public async Task Create_NewButton_IsSourceWithFirstVersionAndLoadedCommand()
    {
        using var environment = new ExtensionTestEnvironment();

        var state = await environment.Manager.CreateAsync("Count walls", null, "Counts the walls.", ExtensionTemplate.Command, null, null, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(state.Descriptor.Id).IsEqualTo("count-walls");
            await Assert.That(state.Descriptor.Kind).IsEqualTo(ExtensionKind.Source);
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Diagnostics.Where(diagnostic => diagnostic.Severity == "error")).IsEmpty();
            await Assert.That(state.Commands).Contains("count-walls.CountWalls");
            await Assert.That(File.Exists(Path.Combine(state.Descriptor.Directory, "src", "CountWalls.cs"))).IsTrue();
            await Assert.That(Directory.EnumerateFiles(state.Descriptor.Directory, "*.dll", SearchOption.TopDirectoryOnly)).IsEmpty();
            await Assert.That(environment.History.GetHistory(state.Descriptor.Directory)).HasSingleItem();
            await Assert.That(environment.History.GetChanges(state.Descriptor.Directory)).IsEmpty();
        }
    }

    [Test]
    public async Task Create_EveryTemplate_Compiles()
    {
        using var environment = new ExtensionTestEnvironment();

        var command = await environment.Manager.CreateAsync("A", null, null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        var withPage = await environment.Manager.CreateAsync("B", null, null, ExtensionTemplate.CommandWithPage, null, null, CancellationToken.None);
        var page = await environment.Manager.CreateAsync("C", null, null, ExtensionTemplate.Page, null, null, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(command.Commands).HasSingleItem();
            await Assert.That(withPage.Commands).HasSingleItem();
            await Assert.That(withPage.Descriptor.OpensPage).IsTrue();
            await Assert.That(page.Descriptor.Kind).IsEqualTo(ExtensionKind.Page);
            await Assert.That(page.Commands).IsEmpty();
        }
    }

    [Test]
    public async Task Invoke_SourceCommand_RunsThroughTheSdkContract()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(), CancellationToken.None);

        var descriptor = environment.Registry.Find("echo.Echo")!;
        var payload = JsonDocument.Parse("""{"x":1}""").RootElement;
        var context = new CommandContext(descriptor, new CommandRequest("echo.Echo", payload, CommandSources.Mcp), new EmptyServices());
        var result = (JsonElement?)await descriptor.Invoke(context.Services, context, new JsonSerializerOptions());

        using (Assert.Multiple())
        {
            await Assert.That(state.Descriptor.Id).IsEqualTo("echo");
            await Assert.That(result!.Value.GetProperty("answer").GetInt32()).IsEqualTo(42);
            await Assert.That(result.Value.GetProperty("got").GetString()).IsEqualTo("""{"x":1}""");
            await Assert.That(descriptor.Annotations!.ExposeToMcp).IsTrue();
            await Assert.That(descriptor.Annotations.ReadOnly).IsTrue();
        }
    }

    [Test]
    public async Task WriteFile_BrokenCode_KeepsThePreviousBuildAndReportsTheLine()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(), CancellationToken.None);

        var broken = await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", "public sealed class Echo {\n  int x = undefinedName;\n}", CancellationToken.None);

        var errors = broken!.Diagnostics.Where(diagnostic => diagnostic.Severity == "error").ToList();
        using (Assert.Multiple())
        {
            await Assert.That(broken.UsesOlderBuild).IsTrue();
            await Assert.That(broken.Commands).Contains("echo.Echo");
            await Assert.That(errors.Select(error => error.File.Replace('\\', '/')).Distinct()).IsEquivalentTo(["src/Echo.cs"]);
            await Assert.That(errors.Select(error => error.Line)).Contains(2);
        }
    }

    [Test]
    public async Task Versions_SaveDiffRestore_NothingIsLost()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        var folder = state.Descriptor.Directory;
        await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(answer: "1"), CancellationToken.None);
        var first = environment.Manager.SaveVersion("echo", "answer 1")!;
        await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(answer: "2"), CancellationToken.None);

        var unsaved = environment.History.Diff(folder, null, null);
        await environment.Manager.RestoreAsync("echo", first.Id, null, CancellationToken.None);
        var history = environment.History.GetHistory(folder);

        using (Assert.Multiple())
        {
            await Assert.That(unsaved.Select(diff => diff.File)).Contains("src/Echo.cs");
            await Assert.That(unsaved.Single(diff => diff.File == "src/Echo.cs").After).Contains("answer = 2");
            await Assert.That(File.ReadAllText(Path.Combine(folder, "src", "Echo.cs"))).Contains("answer = 1");
            // created, answer 1, «before restoring» (answer 2), restored
            await Assert.That(history.Count).IsEqualTo(4);
            await Assert.That(history[1].Message).StartsWith("Before restoring");
            await Assert.That(environment.History.GetChanges(folder)).IsEmpty();
        }
    }

    [Test]
    public async Task Pin_OlderVersion_RunsItsCodeWithoutTouchingTheFiles()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(answer: "1"), CancellationToken.None);
        var first = environment.Manager.SaveVersion("echo", "answer 1")!;
        await environment.Manager.WriteFileAsync("echo", "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(answer: "2"), CancellationToken.None);
        environment.Manager.SaveVersion("echo", "answer 2");

        var pinned = await environment.Manager.PinAsync("echo", first.ShortId, CancellationToken.None);
        var pinnedAnswer = await InvokeAsync(environment, "echo.Echo");
        var unpinned = await environment.Manager.PinAsync("echo", null, CancellationToken.None);
        var currentAnswer = await InvokeAsync(environment, "echo.Echo");

        using (Assert.Multiple())
        {
            await Assert.That(pinned!.LoadedVersion).IsEqualTo(first.Id);
            await Assert.That(pinnedAnswer).IsEqualTo(1);
            await Assert.That(File.ReadAllText(Path.Combine(state.Descriptor.Directory, "src", "Echo.cs"))).Contains("answer = 2");
            await Assert.That(unpinned!.LoadedVersion).IsNull();
            await Assert.That(currentAnswer).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Load_DllExtensionOf1x_RegistersItsCommandsWithTheIdPrefix()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "acme.sample", "2025")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "acme.sample", "version": "1.0.0", "entryAssembly": "Acme.Sample.dll", "ui": { "button": { "name": "Hello", "command": "acme.sample.Hello" } } }""");
        var image = AnalyseTool.Extensions.Building.ExtensionCompiler.Compile([("Hello.cs", ExtensionTestEnvironment.EchoCommand("Hello"))], "Acme.Sample");
        File.WriteAllBytes(Path.Combine(folder, "2025", "Acme.Sample.dll"), image.Assembly!);

        await environment.Host.ReloadAllAsync();
        var state = environment.Host.GetState("acme.sample")!;

        using (Assert.Multiple())
        {
            await Assert.That(state.Descriptor.Kind).IsEqualTo(ExtensionKind.Dll);
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Commands).IsEquivalentTo(["acme.sample.Hello"]);
            await Assert.That(await InvokeAsync(environment, "acme.sample.Hello")).IsEqualTo(42);
        }
    }

    [Test]
    public async Task SetEnabled_Off_RemovesTheCommands()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);

        var off = await environment.Manager.SetEnabledAsync("echo", false, CancellationToken.None);

        await Assert.That(off!.Commands).IsEmpty();
        await Assert.That(environment.Registry.Find("echo.Echo")).IsNull();
    }

    [Test]
    public async Task Remove_MovesTheFolderToTheTrashWithItsHistory()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);

        var movedTo = await environment.Manager.RemoveAsync("echo");

        using (Assert.Multiple())
        {
            await Assert.That(Directory.Exists(state.Descriptor.Directory)).IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(movedTo, ".git"))).IsTrue();
            await Assert.That(environment.Host.GetState("echo")).IsNull();
            await Assert.That(environment.Registry.Find("echo.Echo")).IsNull();
        }
    }

    [Test]
    public async Task ReadFile_PathOutsideTheFolder_IsRefused()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateAsync("Echo", "echo", null, ExtensionTemplate.Command, null, null, CancellationToken.None);

        await Assert.That(() => environment.Manager.ReadFile("echo", "../extensions-state.json")).Throws<CommandException>();
        await Assert.That(() => environment.Manager.ReadFile("echo", ".git/config")).Throws<CommandException>();
    }

    private static async Task<int> InvokeAsync(ExtensionTestEnvironment environment, string command)
    {
        var descriptor = environment.Registry.Find(command)!;
        var context = new CommandContext(descriptor, new CommandRequest(command, default, CommandSources.Mcp), new EmptyServices());
        var result = (JsonElement?)await descriptor.Invoke(context.Services, context, new JsonSerializerOptions());
        return result!.Value.GetProperty("answer").GetInt32();
    }

    /// <summary>The Echo command needs no service: the dispatcher is never asked for.</summary>
    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(AnalyseTool.Hosting.Threading.IRevitDispatcher) ? new NoRevit() : null;
        }
    }

    private sealed class NoRevit : AnalyseTool.Hosting.Threading.IRevitDispatcher
    {
        public Task<T> InvokeAsync<T>(Func<Autodesk.Revit.UI.UIApplication, T> work, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No Revit in this test.");
        }

        public Task InvokeAsync(Action<Autodesk.Revit.UI.UIApplication> work, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No Revit in this test.");
        }
    }
}
