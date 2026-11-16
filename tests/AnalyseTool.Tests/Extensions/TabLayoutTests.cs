using System.IO;
using System.Text.Json.Nodes;
using AnalyseTool.Extensions;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Features.Pane;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Infrastructure.Ribbon;

namespace AnalyseTool.Tests.Extensions;

/// <summary>
///     The layout of pyRevit: <c>&lt;Tab&gt;.tab\&lt;Panel&gt;.panel\&lt;Button&gt;.pushbutton\</c>. Every button folder is an
///     extension of its own — its code, its page, its build, its history.
/// </summary>
[NotInParallel("extensions")]
public sealed class TabLayoutTests
{
    [Test]
    public async Task Create_MakesTabPanelAndButtonFolders_TheButtonCompilesWithItsOwnHistory()
    {
        using var environment = new ExtensionTestEnvironment();

        var state = await environment.Manager.CreateAsync("Count walls", "Counts the walls.", ExtensionTemplate.Command, null, null, "Walls", CancellationToken.None);

        var directory = state.Descriptor.Directory;
        var expected = Path.Combine(environment.Paths.AuthoringRoot, "Walls.tab", "Walls.panel", "Count walls.pushbutton");
        using (Assert.Multiple())
        {
            await Assert.That(directory).IsEqualTo(expected);
            await Assert.That(state.Descriptor.Id).IsEqualTo("walls.count-walls");
            await Assert.That(state.Descriptor.Tab!.Id).IsEqualTo("walls.tab");
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Diagnostics.Where(diagnostic => diagnostic.Severity == "error")).IsEmpty();
            await Assert.That(state.Commands).Contains("walls.count-walls.CountWalls");
            await Assert.That(File.Exists(Path.Combine(directory, "CountWalls.cs"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(directory, ButtonFolder.FileName))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(directory, "AGENTS.md"))).IsTrue();
            await Assert.That(Directory.Exists(Path.Combine(directory, ".git"))).IsTrue();
            await Assert.That(environment.History.GetHistory(directory)).HasSingleItem();
            // bundle.json names the class only: the folder is the id
            await Assert.That(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, ButtonFolder.FileName)))!["command"]!.GetValue<string>()).IsEqualTo("CountWalls");
            await Assert.That(state.Descriptor.Manifest.Ui!.EffectiveButtons().Single().Panel ?? state.Descriptor.Manifest.Ui.Panel).IsEqualTo("Walls");
        }
    }

    [Test]
    public async Task Panels_FollowTheLayout_AndAMoveCarriesTheFolderWithItsHistory()
    {
        using var environment = new ExtensionTestEnvironment();
        var tab = await environment.Manager.CreateTabAsync("Sheets", null, CancellationToken.None);
        await environment.Manager.AddTabButtonAsync(tab.Id, "Renumber", null, ExtensionTemplate.Command, "Numbering", CancellationToken.None);
        await environment.Manager.AddTabButtonAsync(tab.Id, "Export", null, ExtensionTemplate.Command, "Output", CancellationToken.None);
        await environment.Manager.AddTabButtonAsync(tab.Id, "Print", null, ExtensionTemplate.Command, "Output", CancellationToken.None);

        var moved = await environment.Manager.MoveTabButtonAsync(tab.Id, "Renumber", "Output", "Print", null, CancellationToken.None);

        var read = environment.Manager.GetTab(tab.Id);
        using (Assert.Multiple())
        {
            await Assert.That(read.Panels.Select(panel => panel.Name)).IsEquivalentTo(["Numbering", "Output"]);
            await Assert.That(read.Panels[1].Buttons.Select(Path.GetFileName)).IsEquivalentTo(["Export.pushbutton", "Renumber.pushbutton", "Print.pushbutton"]);
            await Assert.That(moved.Descriptor.Id).IsEqualTo("sheets.renumber");
            await Assert.That(moved.Descriptor.Directory).IsEqualTo(Path.Combine(tab.Directory, "Output.panel", "Renumber.pushbutton"));
            await Assert.That(moved.Errors).IsEmpty();
            // Its history went along (a move changes none of its files: nothing new to save)
            await Assert.That(environment.History.GetHistory(moved.Descriptor.Directory)[^1].Message).IsEqualTo("Created «Renumber»");
        }
    }

    [Test]
    public async Task MoveToAnotherTab_ChangesTheId_AndItsPageCallsTheNewName()
    {
        using var environment = new ExtensionTestEnvironment();
        var sheets = await environment.Manager.CreateTabAsync("Sheets", null, CancellationToken.None);
        var rooms = await environment.Manager.CreateTabAsync("Rooms", null, CancellationToken.None);
        await environment.Manager.AddTabButtonAsync(sheets.Id, "Count", null, ExtensionTemplate.CommandWithPage, null, CancellationToken.None);

        var moved = await environment.Manager.MoveTabButtonAsync(sheets.Id, "Count", "Tools", null, rooms.Id, CancellationToken.None);

        var page = File.ReadAllText(Path.Combine(moved.Descriptor.Directory, "index.html"));
        using (Assert.Multiple())
        {
            await Assert.That(moved.Descriptor.Id).IsEqualTo("rooms.count");
            await Assert.That(moved.Commands).Contains("rooms.count.Count");
            await Assert.That(page).Contains("'rooms.count.Count'");
            await Assert.That(page).DoesNotContain("sheets.count");
            await Assert.That(environment.Host.GetState("sheets.count")).IsNull();
            await Assert.That(environment.Manager.GetTab(rooms.Id).Panels.Single().Name).IsEqualTo("Tools");
        }
    }

    [Test]
    public async Task ATabTurnedOff_LoadsNoneOfItsButtons()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateAsync("Count walls", null, ExtensionTemplate.Command, null, null, "Walls", CancellationToken.None);

        await environment.Manager.SetTabEnabledAsync("walls.tab", false, CancellationToken.None);
        var off = environment.Host.GetState(state.Descriptor.Id)!;
        await environment.Manager.SetTabEnabledAsync("walls.tab", true, CancellationToken.None);
        var on = environment.Host.GetState(state.Descriptor.Id)!;

        using (Assert.Multiple())
        {
            await Assert.That(off.Enabled).IsFalse();
            await Assert.That(off.Commands).IsEmpty();
            await Assert.That(on.Enabled).IsTrue();
            await Assert.That(on.Commands).IsNotEmpty();
        }
    }

    [Test]
    public async Task RenamePanel_RenamesItsFolder_RenameButton_WritesBundleJsonOnly()
    {
        using var environment = new ExtensionTestEnvironment();
        var tab = await environment.Manager.CreateTabAsync("Sheets", null, CancellationToken.None);
        var state = await environment.Manager.AddTabButtonAsync(tab.Id, "Renumber", null, ExtensionTemplate.Page, "Numbering", CancellationToken.None);

        await environment.Manager.SetTabPanelAsync(tab.Id, "Numbering", "Numbers", CancellationToken.None);
        var renamed = await environment.Manager.UpdateButtonAsync("sheets.renumber", "Renumber", "Renumber sheets", "From 101.", null, null, CancellationToken.None);

        var bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(renamed.Descriptor.Directory, ButtonFolder.FileName)))!.AsObject();
        using (Assert.Multiple())
        {
            await Assert.That(Directory.Exists(Path.Combine(tab.Directory, "Numbers.panel"))).IsTrue();
            await Assert.That(Directory.Exists(Path.Combine(tab.Directory, "Numbering.panel"))).IsFalse();
            await Assert.That(renamed.Descriptor.Id).IsEqualTo(state.Descriptor.Id);
            await Assert.That(bundle["name"]!.GetValue<string>()).IsEqualTo("Renumber sheets");
            await Assert.That(bundle["tooltip"]!.GetValue<string>()).IsEqualTo("From 101.");
            await Assert.That(bundle["page"]!.GetValue<string>()).IsEqualTo("index.html");
            await Assert.That(bundle.ContainsKey("panel")).IsFalse();
            await Assert.That(bundle.ContainsKey("entryHtml")).IsFalse();
        }
    }

    [Test]
    public async Task HandMadeFolders_PulldownAndShared_AreRead()
    {
        using var environment = new ExtensionTestEnvironment();
        var tab = Path.Combine(environment.Paths.AuthoringRoot, "Tools.tab");
        var shared = Directory.CreateDirectory(Path.Combine(tab, ButtonFolder.SharedFolder)).FullName;
        File.WriteAllText(Path.Combine(shared, "Texts.cs"), "public static class Texts { public const string Hello = \"Hello\"; }\n");
        var button = Directory.CreateDirectory(Path.Combine(tab, "Main.panel", "More.pulldown", "Hello.pushbutton")).FullName;
        File.WriteAllText(Path.Combine(button, ButtonFolder.FileName), """{ "command": "Hello" }""");
        File.WriteAllText(Path.Combine(button, "Hello.cs"), """
                                                            using AnalyseTool.Sdk;
                                                            [RevitCommand(Description = "Says hello.")]
                                                            public sealed class Hello : IRevitTask
                                                            {
                                                                public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken) => Task.FromResult<object?>(Texts.Hello);
                                                            }
                                                            """);

        await environment.Host.ReloadAllAsync();

        var state = environment.Host.GetState("tools.hello")!;
        var top = state.Descriptor.Manifest.Ui!.EffectiveButtons().Single();
        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths, environment.Registry, extensions: environment.Catalog, store: environment.Store)
            .ExecuteAsync(new CommandContext(null, new CommandRequest(nameof(GetPaneButtons), default, CommandSources.WebView), new NoServices()));
        var folder = overview.Folders.Single(item => item.Id == "tools.tab");
        using (Assert.Multiple())
        {
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Commands).Contains("tools.hello.Hello");
            await Assert.That(top.ResolvedKind).IsEqualTo(ButtonKind.Pulldown);
            await Assert.That(top.Name).IsEqualTo("More");
            await Assert.That(top.Items!.Single().Name).IsEqualTo("Hello");
            await Assert.That(folder.Name).IsEqualTo("Tools");
            await Assert.That(folder.Panels).IsEquivalentTo(["Main"]);
            await Assert.That(folder.Buttons.Single().Key).IsEqualTo("tools.hello/Hello");
            await Assert.That(folder.Buttons.Single().Group).IsEqualTo("More");
        }
    }

    [Test]
    public async Task Pane_ShowsATabAsOneFolder_EvenWithoutButtons()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateTabAsync("Empty", null, CancellationToken.None);
        var sheets = await environment.Manager.CreateTabAsync("Sheets", null, CancellationToken.None);
        await environment.Manager.AddTabButtonAsync(sheets.Id, "B", null, ExtensionTemplate.Page, "Second", CancellationToken.None);
        await environment.Manager.AddTabButtonAsync(sheets.Id, "A", null, ExtensionTemplate.Page, "First", CancellationToken.None);

        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths, environment.Registry, extensions: environment.Catalog, store: environment.Store)
            .ExecuteAsync(new CommandContext(null, new CommandRequest(nameof(GetPaneButtons), default, CommandSources.WebView), new NoServices()));

        var folder = overview.Folders.Single(item => item.Id == "sheets.tab");
        using (Assert.Multiple())
        {
            await Assert.That(overview.Folders.Single(item => item.Id == "empty.tab").Buttons).IsEmpty();
            await Assert.That(folder.CanAddButtons).IsTrue();
            await Assert.That(folder.Panels).IsEquivalentTo(["Second", "First"]);
            await Assert.That(folder.Buttons.Select(button => button.Key)).IsEquivalentTo(["sheets.b/B", "sheets.a/A"]);
            await Assert.That(folder.Buttons.Select(button => button.Panel)).IsEquivalentTo(["Second", "First"]);
        }
    }

    [Test]
    public async Task MoveToTab_AnOldButtonBecomesAButtonFolder_TheOtherStays_AndAnEmptyOldFolderGoes()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreatePluginFolderAsync("Sheets", "sheets", null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Sheet list", null, ExtensionTemplate.CommandWithPage, CancellationToken.None);
        var helper = Path.Combine(environment.Host.GetState("sheets")!.Descriptor.Directory, "src", "Helper.cs");
        File.WriteAllText(helper, "public static class Helper { public static int Two => 2; }\n");
        var tab = await environment.Manager.CreateTabAsync("Tools", null, CancellationToken.None);

        var moved = await environment.Manager.MoveToTabAsync("sheets", "Sheet list", tab.Id, "Lists", null, CancellationToken.None);

        var old = environment.Host.GetState("sheets")!;
        var directory = moved.Descriptor.Directory;
        var bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, ButtonFolder.FileName)))!.AsObject();
        using (Assert.Multiple())
        {
            await Assert.That(directory).IsEqualTo(Path.Combine(tab.Directory, "Lists.panel", "Sheet list.pushbutton"));
            await Assert.That(moved.Descriptor.Id).IsEqualTo("tools.sheet-list");
            await Assert.That(moved.Errors).IsEmpty();
            await Assert.That(moved.Commands).Contains("tools.sheet-list.SheetList");
            await Assert.That(File.ReadAllText(Path.Combine(directory, "index.html"))).Contains("'tools.sheet-list.SheetList'");
            await Assert.That(File.Exists(Path.Combine(directory, "SheetList.cs"))).IsTrue();
            // A helper without a command is copied: the old folder may still need it
            await Assert.That(File.Exists(Path.Combine(directory, "Helper.cs"))).IsTrue();
            await Assert.That(File.Exists(helper)).IsTrue();
            await Assert.That(bundle["page"]!.GetValue<string>()).IsEqualTo("index.html");
            await Assert.That(bundle.ContainsKey("panel")).IsFalse();
            await Assert.That(environment.History.GetHistory(directory)).HasSingleItem();
            // The old folder keeps its other button, without the moved one and its files
            await Assert.That(old.Descriptor.Manifest.Ui!.EffectiveButtons().Select(button => button.Name)).IsEquivalentTo(["Sheets"]);
            await Assert.That(File.Exists(Path.Combine(old.Descriptor.Directory, "src", "SheetList.cs"))).IsFalse();
            await Assert.That(old.Errors).IsEmpty();
        }

        // The last button: the old folder is gone (to the trash), the button lives in the tab
        File.Delete(helper);
        var last = await environment.Manager.MoveToTabAsync("sheets", "Sheets", tab.Id, "Lists", "Sheet list", CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(environment.Host.GetState("sheets")).IsNull();
            await Assert.That(last.Commands).IsNotEmpty();
            await Assert.That(environment.Manager.GetTab(tab.Id).Panels.Single().Buttons.Select(Path.GetFileName)).IsEquivalentTo(["Sheets.pushbutton", "Sheet list.pushbutton"]);
        }
    }

    [Test]
    public async Task Pane_PutsTheUsersPluginJsonFolders_IntoOlderButtons()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreatePluginFolderAsync("Count walls", null, null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        await environment.Manager.CreateAsync("Renumber", null, ExtensionTemplate.Command, null, null, "Sheets", CancellationToken.None);

        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths, environment.Registry, extensions: environment.Catalog, store: environment.Store)
            .ExecuteAsync(new CommandContext(null, new CommandRequest(nameof(GetPaneButtons), default, CommandSources.WebView), new NoServices()));

        var older = overview.Folders.Single(item => item.Id == ButtonCatalog.OlderFolderId);
        using (Assert.Multiple())
        {
            await Assert.That(overview.Folders.Select(item => item.Id)).DoesNotContain("count-walls");
            await Assert.That(overview.Folders.Select(item => item.Id)).Contains("sheets.tab");
            await Assert.That(older.CanAddButtons).IsFalse();
            await Assert.That(older.Parts!.Single().Id).IsEqualTo("count-walls");
            await Assert.That(older.Buttons.Single().Key).IsEqualTo("count-walls/Count walls");
            await Assert.That(older.Buttons.Single().Panel).IsEqualTo(older.Parts!.Single().Name);
        }
    }

    [Test]
    public async Task Pane_ShowsADllExtensionAsAFolderOfItsOwn_OnTheTabItNames_NotInOlderButtons()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "acme.doors", "2025")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """
                                                               { "schema": 2, "id": "acme.doors", "name": "Doors", "version": "1.0.0", "build": "author", "entryAssembly": "Acme.Doors.dll",
                                                                 "ui": { "tab": "Acme", "panel": "Doors", "buttons": [ { "name": "Count doors", "command": "acme.doors.CountDoors" } ] } }
                                                               """);
        var image = AnalyseTool.Extensions.Building.ExtensionCompiler.Compile([("CountDoors.cs", ExtensionTestEnvironment.EchoCommand("CountDoors"))], "Acme.Doors");
        File.WriteAllBytes(Path.Combine(folder, "2025", "Acme.Doors.dll"), image.Assembly!);
        await environment.Host.ReloadAllAsync();

        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths, environment.Registry, extensions: environment.Catalog, store: environment.Store)
            .ExecuteAsync(new CommandContext(null, new CommandRequest(nameof(GetPaneButtons), default, CommandSources.WebView), new NoServices()));

        var doors = overview.Folders.SingleOrDefault(item => item.Id == "acme.doors");
        using (Assert.Multiple())
        {
            await Assert.That(environment.Host.GetState("acme.doors")!.Commands).Contains("acme.doors.CountDoors");
            await Assert.That(doors).IsNotNull();
            await Assert.That(doors!.Tab).IsEqualTo("Acme");
            await Assert.That(doors.Buttons.Single().Panel).IsEqualTo("Doors");
            await Assert.That(overview.Folders.Select(item => item.Id)).DoesNotContain(ButtonCatalog.OlderFolderId);
        }
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
