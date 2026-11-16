using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Authoring;
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

        var overview = await new GetPaneButtons(new ButtonCatalog(environment.Host), environment.Paths).ExecuteAsync(Context(nameof(GetPaneButtons)));

        var builtIn = overview.Folders[0];
        var walls = overview.Folders.Single(folder => folder.Id == "count-walls");
        using (Assert.Multiple())
        {
            await Assert.That(builtIn.Id).IsEqualTo(ButtonCatalog.BuiltInId);
            await Assert.That(builtIn.BuiltIn).IsTrue();
            // "ribbon": false buttons are tiles; "pane": false ones (the pane itself, refresh, new button) are not
            await Assert.That(builtIn.Buttons.Select(button => button.Key)).Contains("analysetool/Parameters");
            await Assert.That(builtIn.Buttons.Select(button => button.Key)).DoesNotContain("analysetool/AnalyseTool");
            await Assert.That(builtIn.Buttons.Select(button => button.Key)).DoesNotContain("analysetool/Refresh");
            await Assert.That(builtIn.Buttons.Single(button => button.Name == "Settings").Glyph).IsEqualTo("");
            await Assert.That(walls.Buttons.Single().Key).IsEqualTo("count-walls/Count walls");
            await Assert.That(walls.Buttons.Single().Command).IsEqualTo("count-walls.CountWalls");
            await Assert.That(walls.Problems).IsEmpty();
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
        await new SavePaneProfile(environment.Paths, new RibbonFavorites(environment.Paths)).ExecuteAsync(new SavePaneProfileRequest(profile), Context(nameof(SavePaneProfile)));
        var after = await new GetPaneProfile(environment.Paths).ExecuteAsync(Context(nameof(GetPaneProfile)));
        await File.WriteAllTextAsync(Path.Combine(environment.Paths.ProfileRoot, "pane-profile.json"), "{ broken");
        var broken = await new GetPaneProfile(environment.Paths).ExecuteAsync(Context(nameof(GetPaneProfile)));

        using (Assert.Multiple())
        {
            await Assert.That(before.Profile).IsNull();
            await Assert.That(after.Profile!.Value.GetProperty("size").GetString()).IsEqualTo("large");
            await Assert.That(after.Profile!.Value.GetProperty("dock")[0].GetString()).IsEqualTo("analysetool/Statistics");
            await Assert.That(broken.Profile).IsNull();
            await Assert.That(async () => await new SavePaneProfile(environment.Paths, new RibbonFavorites(environment.Paths)).ExecuteAsync(new SavePaneProfileRequest(JsonDocument.Parse("[]").RootElement), Context(nameof(SavePaneProfile))))
                .Throws<CommandException>();
        }
    }

    [Test]
    public async Task Stars_OfASavedProfile_GoToTheRibbon_AndRebuildItOnlyWhenTheyChange()
    {
        using var environment = new ExtensionTestEnvironment();
        var favorites = new RibbonFavorites(environment.Paths);
        var changes = 0;
        favorites.Changed += (_, _) => changes++;
        var save = new SavePaneProfile(environment.Paths, favorites);

        await save.ExecuteAsync(new SavePaneProfileRequest(JsonDocument.Parse("""{ "ribbon": ["analysetool/Statistics", "count-walls/Count walls", "analysetool/Statistics", 5] }""").RootElement), Context(nameof(SavePaneProfile)));
        await save.ExecuteAsync(new SavePaneProfileRequest(JsonDocument.Parse("""{ "size": "large", "ribbon": ["analysetool/Statistics", "count-walls/Count walls"] }""").RootElement), Context(nameof(SavePaneProfile)));

        using (Assert.Multiple())
        {
            await Assert.That(favorites.Keys).IsEquivalentTo(["analysetool/Statistics", "count-walls/Count walls"]);
            await Assert.That(changes).IsEqualTo(1);
            // A new start of Revit reads them from the file
            await Assert.That(new RibbonFavorites(environment.Paths).Keys.Count).IsEqualTo(2);
            await Assert.That(RibbonFavorites.Parse(null)).IsEmpty();
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
