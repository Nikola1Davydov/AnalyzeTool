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
        var state = await environment.Manager.AddButtonAsync("sheets", "Help", null, ExtensionTemplate.Page, CancellationToken.None);

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

        await environment.Manager.UpdateFolderAsync("sheets", "Sheet tools", "Everything about sheets.", CancellationToken.None);
        var state = await environment.Manager.UpdateButtonAsync("sheets", "Renumber", "Renumber sheets", "From 101 up.", CancellationToken.None);
        var taken = await Assert.That(async () => await environment.Manager.UpdateButtonAsync("sheets", "Renumber sheets", "Check", null, CancellationToken.None))
            .Throws<CommandException>();

        var renamed = state.Descriptor.Manifest.Ui!.EffectiveButtons()[0];
        using (Assert.Multiple())
        {
            await Assert.That(state.Descriptor.Manifest.Name).IsEqualTo("Sheet tools");
            await Assert.That(state.Descriptor.Manifest.Description).IsEqualTo("Everything about sheets.");
            await Assert.That(renamed.Name).IsEqualTo("Renumber sheets");
            await Assert.That(renamed.Tooltip).IsEqualTo("From 101 up.");
            // The code keeps its file and its command
            await Assert.That(renamed.Command).IsEqualTo("sheets.Renumber");
            await Assert.That(state.Commands).Contains("sheets.Renumber");
            await Assert.That(taken).IsNotNull();
            await Assert.That(environment.History.GetHistory(state.Descriptor.Directory)[0].Message).IsEqualTo("Renamed «Renumber» to «Renumber sheets»");
        }
    }

    [Test]
    public async Task AddButton_WithANameTaken_IsRefused_AndAnOldSingleButtonBecomesAList()
    {
        using var environment = new ExtensionTestEnvironment();
        var first = await environment.Manager.CreateAsync("Count walls", null, null, ExtensionTemplate.Command, null, null, CancellationToken.None);
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
