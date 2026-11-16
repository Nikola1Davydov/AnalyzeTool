using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Features.Pane;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Infrastructure.Ribbon;
using AnalyseTool.Tests.Extensions;

namespace AnalyseTool.Tests.Ribbon;

/// <summary>The dock pane: every ribbon button as a tile, and the profile of the pane in the profile folder.</summary>
[NotInParallel("extensions")]
public sealed class PaneTests
{
    [Test]
    public async Task Buttons_ShowAnalyseToolFirst_ThenTheExtensions_WithTheirKeysAndGlyphs()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateAsync("Count walls", null, null, ExtensionTemplate.Command, null, null, CancellationToken.None);

        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths, environment.Registry).ExecuteAsync(Context(nameof(GetPaneButtons)));

        var builtIn = overview.Folders[0];
        var walls = overview.Folders.Single(folder => folder.Id == "count-walls");
        using (Assert.Multiple())
        {
            await Assert.That(builtIn.Id).IsEqualTo(ButtonCatalog.BuiltInId);
            await Assert.That(builtIn.BuiltIn).IsTrue();
            // "ribbon": false buttons are tiles; "pane": false ones (the pane itself, refresh, new button) are not
            await Assert.That(builtIn.Buttons.Select(button => button.Key)).Contains("analysetool/Parameters");
            await Assert.That(builtIn.Buttons.Select(button => button.Key)).DoesNotContain("analysetool/Buttons");
            await Assert.That(builtIn.Buttons.Select(button => button.Key)).DoesNotContain("analysetool/Refresh");
            await Assert.That(builtIn.Buttons.Single(button => button.Name == "Parameters").Glyph).IsEqualTo("\uE9F9");
            await Assert.That(walls.Buttons.Single().Key).IsEqualTo("count-walls/Count walls");
            await Assert.That(walls.Buttons.Single().Command).IsEqualTo("count-walls.CountWalls");
            await Assert.That(walls.Problems).IsEmpty();
        }
    }

    [Test]
    public async Task Buttons_ACommandWithInputGetsAForm_ADockablePageOpensInThePane()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateFolderAsync("Sheets", null, null, null, null, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "Renumber", null, ExtensionTemplate.Command, CancellationToken.None);
        await environment.Manager.AddButtonAsync("sheets", "List", null, ExtensionTemplate.Page, CancellationToken.None);
        await environment.Manager.WriteFileAsync("sheets", "src/Renumber.cs", """
            using AnalyseTool.Sdk;

            [RevitCommand(Description = "Renumbers.", InputType = typeof(RenumberInput))]
            public sealed class Renumber : IRevitTask
            {
                public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken) => Task.FromResult<object?>("ok");
            }

            public sealed class RenumberInput
            {
                public int Start { get; set; } = 101;
            }
            """, CancellationToken.None);
        await environment.Manager.UpdateButtonAsync("sheets", "List", "List", null, null, null, CancellationToken.None, dockable: true);

        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths, environment.Registry).ExecuteAsync(Context(nameof(GetPaneButtons)));

        var buttons = overview.Folders.Single(folder => folder.Id == "sheets").Buttons;
        var renumber = buttons.Single(button => button.Name == "Renumber");
        var list = buttons.Single(button => button.Name == "List");
        using (Assert.Multiple())
        {
            await Assert.That(renumber.Input).IsNotNull();
            await Assert.That(renumber.Input!.Value.GetProperty("properties").EnumerateObject().Select(property => property.Name.ToLowerInvariant())).Contains("start");
            await Assert.That(list.Input).IsNull();
            await Assert.That(list.HasPage).IsTrue();
            await Assert.That(list.Dockable).IsTrue();
        }
    }

    [Test]
    public async Task Icon_OutsideItsFolder_IsNotRead()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Root, "ext")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(environment.Root, "secret.png"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(folder, "icon.png"), [1, 2, 3]);

        using (Assert.Multiple())
        {
            await Assert.That(GetPaneButtons.Icon(folder, @"..\secret.png").Image).IsNull();
            await Assert.That(GetPaneButtons.Icon(folder, "icon.png").Image).IsEqualTo("data:image/png;base64,AQID");
            await Assert.That(GetPaneButtons.Icon(folder, "glyph:E713").Glyph).IsEqualTo("");
            await Assert.That(GetPaneButtons.Icon(folder, "glyph:nonsense").Glyph).IsNull();
        }
    }

    [Test]
    public async Task Profile_IsSavedInTheProfileFolder_AndABrokenFileReadsAsNone()
    {
        using var environment = new ExtensionTestEnvironment();
        var profile = JsonDocument.Parse("""{ "size": "large", "dock": ["analysetool/Statistics"] }""").RootElement;

        var before = await new GetPaneProfile(environment.Paths).ExecuteAsync(Context(nameof(GetPaneProfile)));
        await new SavePaneProfile(environment.Paths, new RibbonVisibility(environment.Paths)).ExecuteAsync(new SavePaneProfileRequest(profile), Context(nameof(SavePaneProfile)));
        var after = await new GetPaneProfile(environment.Paths).ExecuteAsync(Context(nameof(GetPaneProfile)));
        await File.WriteAllTextAsync(Path.Combine(environment.Paths.ProfileRoot, "pane-profile.json"), "{ broken");
        var broken = await new GetPaneProfile(environment.Paths).ExecuteAsync(Context(nameof(GetPaneProfile)));

        using (Assert.Multiple())
        {
            await Assert.That(before.Profile).IsNull();
            await Assert.That(after.Profile!.Value.GetProperty("size").GetString()).IsEqualTo("large");
            await Assert.That(after.Profile!.Value.GetProperty("dock")[0].GetString()).IsEqualTo("analysetool/Statistics");
            await Assert.That(broken.Profile).IsNull();
            await Assert.That(async () => await new SavePaneProfile(environment.Paths, new RibbonVisibility(environment.Paths)).ExecuteAsync(new SavePaneProfileRequest(JsonDocument.Parse("[]").RootElement), Context(nameof(SavePaneProfile))))
                .Throws<CommandException>();
        }
    }

    [Test]
    public async Task Visibility_OfASavedProfile_DecidesWhatTheRibbonLoads_AndRebuildsOnlyOnAChange()
    {
        using var environment = new ExtensionTestEnvironment();
        var visibility = new RibbonVisibility(environment.Paths);
        var changes = 0;
        visibility.Changed += (_, _) => changes++;
        var save = new SavePaneProfile(environment.Paths, visibility);
        const string profile = """{ "hiddenFolders": ["old-tools"], "hiddenButtons": ["sheets/Renumber", 5], "ribbon": ["analysetool/Parameters"] }""";

        await save.ExecuteAsync(new SavePaneProfileRequest(JsonDocument.Parse(profile).RootElement), Context(nameof(SavePaneProfile)));
        await save.ExecuteAsync(new SavePaneProfileRequest(JsonDocument.Parse(profile).RootElement), Context(nameof(SavePaneProfile)));

        using (Assert.Multiple())
        {
            await Assert.That(changes).IsEqualTo(1);
            await Assert.That(visibility.IsFolderHidden("OLD-TOOLS")).IsTrue();
            // A button is loaded as its plugin.json says, unless «+»/«−» of the pane says otherwise
            await Assert.That(visibility.IsButtonLoaded("sheets/Renumber", onRibbonByDefault: true)).IsFalse();
            await Assert.That(visibility.IsButtonLoaded("sheets/Check", onRibbonByDefault: true)).IsTrue();
            await Assert.That(visibility.IsButtonLoaded("analysetool/Parameters", onRibbonByDefault: false)).IsTrue();
            await Assert.That(visibility.IsButtonLoaded("analysetool/About", onRibbonByDefault: false)).IsFalse();
            // A new start of Revit reads them from the file
            await Assert.That(new RibbonVisibility(environment.Paths).IsFolderHidden("old-tools")).IsTrue();
        }
    }

    [Test]
    public async Task Place_AFolderOfTheUserIsItsTab_APackageKeepsItsAuthorsTab()
    {
        using var environment = new ExtensionTestEnvironment();
        await environment.Manager.CreateFolderAsync("Sheets", null, null, null, null, CancellationToken.None);
        var state = await environment.Manager.AddButtonAsync("sheets", "Renumber", null, ExtensionTemplate.Command, CancellationToken.None, panel: "Numbers");
        // A tab left in plugin.json by hand is ignored for a folder of the user
        ExtensionManifest.Update(state.Descriptor.Directory, manifest => manifest["ui"]!["tab"] = "Somewhere");
        state = (await environment.Host.ReloadAsync("sheets", CancellationToken.None))!;

        var own = new ButtonSource(state.Descriptor, false, true, state);
        var package = own with { Extension = state.Descriptor with { Zone = ExtensionZone.Managed } };
        var builtIn = ButtonCatalog.LoadBuiltIn(Path.Combine(AppContext.BaseDirectory, ButtonCatalog.RibbonFolder), includeDev: false)[0];
        var button = state.Descriptor.Manifest.Ui!.EffectiveButtons()[0];

        using (Assert.Multiple())
        {
            await Assert.That(ButtonCatalog.Place(own, button)).IsEqualTo(("Sheets", "Numbers"));
            await Assert.That(ButtonCatalog.Place(package, button)).IsEqualTo(("Somewhere", "Numbers"));
            await Assert.That(ButtonCatalog.Place(new ButtonSource(builtIn, true, true), builtIn.Manifest.Ui!.EffectiveButtons()[0]).Tab).IsEqualTo("AnalyseTool");
            await Assert.That(ButtonCatalog.Panels(own)).IsEquivalentTo(["Numbers"]);
        }
    }

    [Test]
    public async Task RunButton_UnknownKey_IsNotFound()
    {
        using var environment = new ExtensionTestEnvironment();
        var command = new RunButton(new ButtonCatalog(environment.Host), null!);

        var exception = await Assert.That(async () => await command.ExecuteAsync(new RunButtonRequest("nobody/Nothing"), Context(nameof(RunButton)))).Throws<CommandException>();

        await Assert.That(exception!.Code).IsEqualTo(CommandErrorCodes.NotFound);
    }

    private static CommandContext Context(string command)
    {
        return new CommandContext(null, new CommandRequest(command, default, CommandSources.WebView), new NoServices());
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
