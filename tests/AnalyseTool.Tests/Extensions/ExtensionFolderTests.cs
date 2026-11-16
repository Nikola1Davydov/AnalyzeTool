using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Tests.Extensions;

/// <summary>A folder of buttons: created empty, buttons added one by one, each addition a version.</summary>
[NotInParallel("extensions")]
public sealed class ExtensionFolderTests
{
    [Test]
    public async Task CreateFolder_IsEmpty_LoadsWithoutErrors_AndHasAVersion()
    {
        using var environment = new ExtensionTestEnvironment();

        var state = await environment.Manager.CreateFolderAsync("Sheets", null, "Sheet tools.", null, null, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(state.Descriptor.Id).IsEqualTo("sheets");
            await Assert.That(state.Descriptor.Manifest.Name).IsEqualTo("Sheets");
            await Assert.That(state.Descriptor.Kind).IsEqualTo(ExtensionKind.Source);
            await Assert.That(state.Descriptor.HasButtons).IsFalse();
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Commands).IsEmpty();
            await Assert.That(Directory.Exists(Path.Combine(state.Descriptor.Directory, "src"))).IsFalse();
            await Assert.That(environment.History.GetHistory(state.Descriptor.Directory)).HasSingleItem();
        }
    }

    [Test]
    public async Task AddButton_ToAFolder_CompilesLoadsAndSavesAVersionEach()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateFolderAsync("Sheets", null, null, null, null, CancellationToken.None);

        await environment.Manager.AddButtonAsync("sheets", "Renumber sheets", "Renumbers.", ExtensionTemplate.Command, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Sheet list", null, ExtensionTemplate.CommandWithPage, CancellationToken.None);
        var state = await environment.Manager.AddButtonAsync("sheets", "Help", null, ExtensionTemplate.Page, CancellationToken.None, panel: "Help");

        var buttons = state.Descriptor.Manifest.Ui!.EffectiveButtons();
        var directory = state.Descriptor.Directory;
        using (Assert.Multiple())
        {
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Commands).Contains("sheets.RenumberSheets");
            await Assert.That(state.Commands).Contains("sheets.SheetList");
            await Assert.That(buttons.Select(button => button.Name)).IsEquivalentTo(["Renumber sheets", "Sheet list", "Help"]);
            await Assert.That(buttons[0].Command).IsEqualTo("sheets.RenumberSheets");
            // The first page is index.html, a further one is named after its button
            await Assert.That(File.Exists(Path.Combine(directory, "index.html"))).IsTrue();
            await Assert.That(buttons[2].EntryHtml).IsEqualTo("Help.html");
            // Advanced at creation: a panel of its own; the others follow the folder
            await Assert.That(buttons[2].Panel).IsEqualTo("Help");
            await Assert.That(buttons[0].Panel).IsNull();
            await Assert.That(File.Exists(Path.Combine(directory, "Help.html"))).IsTrue();
            await Assert.That(environment.History.GetHistory(directory).Count).IsEqualTo(4);
            await Assert.That(environment.History.GetHistory(directory)[0].Message).IsEqualTo("Added the button «Help»");
            await Assert.That(environment.History.GetChanges(directory)).IsEmpty();
        }
    }

    [Test]
    public async Task Rename_FolderAndButton_ChangesPluginJsonOnly_AsVersions()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateFolderAsync("Sheets", null, null, null, null, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Renumber", null, ExtensionTemplate.Command, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Check", null, ExtensionTemplate.Command, CancellationToken.None);

        await environment.Manager.UpdateFolderAsync("sheets", "Sheet tools", "Everything about sheets.", "Sheets", null, CancellationToken.None);
        var state = await environment.Manager.UpdateButtonAsync("sheets", "Renumber", "Renumber sheets", "From 101 up.", null, "Numbers", CancellationToken.None);
        var taken = await Assert.That(async () => await environment.Manager.UpdateButtonAsync("sheets", "Renumber sheets", "Check", null, null, null, CancellationToken.None))
            .Throws<CommandException>();

        var renamed = state.Descriptor.Manifest.Ui!.EffectiveButtons()[0];
        using (Assert.Multiple())
        {
            await Assert.That(state.Descriptor.Manifest.Name).IsEqualTo("Sheet tools");
            await Assert.That(state.Descriptor.Manifest.Description).IsEqualTo("Everything about sheets.");
            await Assert.That(renamed.Name).IsEqualTo("Renumber sheets");
            await Assert.That(renamed.Tooltip).IsEqualTo("From 101 up.");
            // Advanced: the folder's tab, the button's own panel
            await Assert.That(state.Descriptor.Manifest.Ui.Tab).IsEqualTo("Sheets");
            await Assert.That(renamed.Panel).IsEqualTo("Numbers");
            await Assert.That(renamed.Tab).IsNull();
            // The code keeps its file and its command
            await Assert.That(renamed.Command).IsEqualTo("sheets.Renumber");
            await Assert.That(state.Commands).Contains("sheets.Renumber");
            await Assert.That(taken).IsNotNull();
            await Assert.That(environment.History.GetHistory(state.Descriptor.Directory)[0].Message).IsEqualTo("Renamed «Renumber» to «Renumber sheets»");
        }
    }

    [Test]
    public async Task Panels_AddRenameAndMoveWithinAFolder_KeepTheOrder()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateFolderAsync("Sheets", null, null, null, null, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "One", null, ExtensionTemplate.Command, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Two", null, ExtensionTemplate.Command, CancellationToken.None);

        await environment.Manager.SetPanelAsync("sheets", null, "Checks", CancellationToken.None);
        await environment.Manager.MoveButtonAsync("sheets", "Two", "Checks", null, null, CancellationToken.None);
        var state = await environment.Manager.SetPanelAsync("sheets", "Checks", "Audit", CancellationToken.None);
        state = await environment.Manager.MoveButtonAsync("sheets", "Two", "Sheets", "One", null, CancellationToken.None);

        var ui = state.Descriptor.Manifest.Ui!;
        using (Assert.Multiple())
        {
            await Assert.That(ui.Panels!).IsEquivalentTo(["Sheets", "Audit"]);
            // Before «One» in the panel «Sheets»
            await Assert.That(ui.EffectiveButtons().Select(button => button.Name)).IsEquivalentTo(["Two", "One"]);
            await Assert.That(ui.EffectiveButtons()[0].Panel).IsEqualTo("Sheets");
            await Assert.That(environment.History.GetChanges(state.Descriptor.Directory)).IsEmpty();
        }
    }

    [Test]
    public async Task MoveButton_ToAnotherFolder_CarriesItsCodeAndPage_AndRenamesItsCommand()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateFolderAsync("Sheets", null, null, null, null, CancellationToken.None);
        await environment.Manager.CreateFolderAsync("Rooms", null, null, null, null, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Keep", null, ExtensionTemplate.Command, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "List rooms", null, ExtensionTemplate.CommandWithPage, CancellationToken.None);

        var rooms = await environment.Manager.MoveButtonAsync("sheets", "List rooms", "Rooms", null, "rooms", CancellationToken.None);
        var sheets = environment.Manager.Get("sheets");

        var moved = rooms.Descriptor.Manifest.Ui!.EffectiveButtons().Single();
        using (Assert.Multiple())
        {
            await Assert.That(rooms.Errors).IsEmpty();
            await Assert.That(rooms.Commands).Contains("rooms.ListRooms");
            await Assert.That(sheets.Commands).DoesNotContain("sheets.ListRooms");
            await Assert.That(sheets.Commands).Contains("sheets.Keep");
            await Assert.That(File.Exists(Path.Combine(rooms.Descriptor.Directory, "src", "ListRooms.cs"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(sheets.Descriptor.Directory, "src", "ListRooms.cs"))).IsFalse();
            // Its page calls the command by its new name
            var page = await File.ReadAllTextAsync(Path.Combine(rooms.Descriptor.Directory, moved.EntryHtml ?? "index.html"));
            await Assert.That(page).Contains("rooms.ListRooms");
            await Assert.That(page).DoesNotContain("sheets.ListRooms");
            await Assert.That(environment.History.GetHistory(rooms.Descriptor.Directory)[0].Message).IsEqualTo("Took «List rooms» from Sheets");
            await Assert.That(environment.History.GetHistory(sheets.Descriptor.Directory)[0].Message).IsEqualTo("Moved «List rooms» to Rooms");
            await Assert.That(environment.History.GetChanges(sheets.Descriptor.Directory)).IsEmpty();
        }
    }

    [Test]
    public async Task AddButton_WithANameTaken_IsRefused_AndAnOldSingleButtonBecomesAList()
    {
        using var environment = new ExtensionTestEnvironment();
        var first = await environment.Manager.CreatePluginFolderAsync("Count walls", null, null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        // The 1.x form: one "button" object instead of the list
        ExtensionManifest.Update(first.Descriptor.Directory, manifest =>
        {
            var ui = manifest["ui"]!.AsObject();
            var single = ui["buttons"]!.AsArray()[0]!.DeepClone();
            ui.Remove("buttons");
            ui["button"] = single;
        });
        await environment.Host.ReloadAsync("count-walls", CancellationToken.None);

        var taken = await Assert.That(async () => await environment.Manager.AddButtonAsync("count-walls", "Count\nwalls", null, ExtensionTemplate.Command, CancellationToken.None))
            .Throws<CommandException>();
        var state = await environment.Manager.AddButtonAsync("count-walls", "Count doors", null, ExtensionTemplate.Command, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(taken).IsNotNull();
            await Assert.That(state.Descriptor.Manifest.Ui!.Button).IsNull();
            await Assert.That(state.Descriptor.Manifest.Ui.EffectiveButtons().Select(button => button.Name)).IsEquivalentTo(["Count walls", "Count doors"]);
            await Assert.That(state.Commands).Contains("count-walls.CountDoors");
        }
    }
}
