using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Usage;
using AnalyseTool.Features.Statistics;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Infrastructure.Ribbon;
using AnalyseTool.Tests.Extensions;

namespace AnalyseTool.Tests.Ribbon;

/// <summary>Every ribbon button is declared one way (plugin.json) and counted one way (ButtonUsageStore).</summary>
[NotInParallel("extensions")]
public sealed class ButtonTests
{
    [Test]
    public async Task BuiltIn_EveryButton_IsAPluginJsonButtonRunningAnExistingCommand()
    {
        var builtIn = ButtonCatalog.LoadBuiltIn(Path.Combine(AppContext.BaseDirectory, ButtonCatalog.RibbonFolder), includeDev: false);
        var buttons = builtIn.SelectMany(extension => extension.Manifest.Ui!.EffectiveButtons()).ToList();
        var commands = LoadableTypes(typeof(GetButtonStatistics).Assembly)
            .Where(type => type.GetCustomAttributes(typeof(CommandAttribute), false).Length > 0)
            .Select(type => type.Name)
            .ToHashSet();

        using (Assert.Multiple())
        {
            await Assert.That(builtIn).HasSingleItem();
            await Assert.That(builtIn[0].Id).IsEqualTo(ButtonCatalog.BuiltInId);
            await Assert.That(buttons.Select(button => ButtonUsageStore.Normalize(button.Name))).Contains("Statistics");
            foreach (var button in buttons)
            {
                await Assert.That(commands).Contains(button.Command!);
            }

            var statistics = buttons.Single(button => button.Name == "Statistics");
            await Assert.That(statistics.Args!.Value.GetProperty("route").GetString()).IsEqualTo("/statistics");
        }
    }

    [Test]
    public async Task Usage_ClicksAndResults_AreCountedAndSurviveARestart()
    {
        using var environment = new ExtensionTestEnvironment();
        var time = new ManualTime(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
        var store = new ButtonUsageStore(environment.Paths, time);

        store.RecordClick("count-walls", "Count\nwalls", "count-walls.CountWalls");
        store.RecordResult("count-walls", "Count\nwalls", true, TimeSpan.FromMilliseconds(100), null);
        time.Now = time.Now.AddDays(1);
        store.RecordClick("count-walls", "Count walls", "count-walls.CountWalls");
        store.RecordResult("count-walls", "Count walls", false, TimeSpan.FromMilliseconds(300), "Open a document first.");
        store.RecordClick("analysetool", "Settings", "OpenWindow");

        var usage = new ButtonUsageStore(environment.Paths, time).Read();
        var walls = usage.Buttons[0];
        using (Assert.Multiple())
        {
            await Assert.That(usage.Buttons.Count).IsEqualTo(2);
            await Assert.That(walls.Button).IsEqualTo("Count walls");
            await Assert.That(walls.Clicks).IsEqualTo(2);
            await Assert.That(walls.Failures).IsEqualTo(1);
            await Assert.That(walls.LastError).IsEqualTo("Open a document first.");
            await Assert.That(walls.AverageMilliseconds).IsEqualTo(200d);
            await Assert.That(walls.Days.Count).IsEqualTo(2);
            await Assert.That(usage.Since).IsNotNull();
        }
    }

    [Test]
    public async Task Usage_OldDays_AreDropped_AndResetForgetsOneExtension()
    {
        using var environment = new ExtensionTestEnvironment();
        var time = new ManualTime(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var store = new ButtonUsageStore(environment.Paths, time);
        store.RecordClick("a", "One", null);
        time.Now = time.Now.AddDays(ButtonUsageStore.DaysKept + 5);
        store.RecordClick("a", "One", null);
        store.RecordClick("b", "Two", null);

        var before = store.Read().Buttons.Single(button => button.Extension == "a");
        var removed = store.Reset("a");
        var after = store.Read();

        using (Assert.Multiple())
        {
            await Assert.That(before.Clicks).IsEqualTo(2);
            await Assert.That(before.Days).HasSingleItem();
            await Assert.That(removed).IsEqualTo(1);
            await Assert.That(after.Buttons.Select(button => button.Extension)).IsEquivalentTo(["b"]);
        }
    }

    [Test]
    public async Task Usage_BrokenFile_IsSetAsideAndCountingGoesOn()
    {
        using var environment = new ExtensionTestEnvironment();
        var store = new ButtonUsageStore(environment.Paths);
        Directory.CreateDirectory(environment.Paths.ProfileRoot);
        await File.WriteAllTextAsync(store.FilePath, "{ not json");

        store.RecordClick("a", "One", null);

        using (Assert.Multiple())
        {
            await Assert.That(store.Read().Buttons).HasSingleItem();
            await Assert.That(File.Exists(store.FilePath + ".bad")).IsTrue();
        }
    }

    [Test]
    public async Task Statistics_ListEveryButton_UsedOrNot_AndKeepGoneOnes()
    {
        using var environment = new ExtensionTestEnvironment();
        var time = new ManualTime(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
        var store = new ButtonUsageStore(environment.Paths, time);
        await environment.Manager.CreateAsync("Count walls", null, null, ExtensionTemplate.Command, null, null, CancellationToken.None);
        store.RecordClick("count-walls", "Count walls", "count-walls.CountWalls");
        store.RecordClick("count-walls", "Count walls", "count-walls.CountWalls");
        store.RecordClick("removed-tool", "Purge", "removed-tool.Purge");

        var command = new GetButtonStatistics(new ButtonCatalog(environment.Host), store, time);
        var result = await command.ExecuteAsync(new CommandContext(null, new CommandRequest(nameof(GetButtonStatistics), default, CommandSources.WebView), new NoServices()));

        var walls = result.Buttons.Single(button => button.Extension == "count-walls");
        var gone = result.Buttons.Single(button => button.Extension == "removed-tool");
        using (Assert.Multiple())
        {
            await Assert.That(result.TotalClicks).IsEqualTo(3);
            await Assert.That(walls.Clicks).IsEqualTo(2);
            await Assert.That(walls.OnRibbon).IsTrue();
            await Assert.That(walls.Last30Days.Count).IsEqualTo(GetButtonStatistics.Days);
            await Assert.That(walls.Last30Days[^1]).IsEqualTo(2);
            await Assert.That(gone.OnRibbon).IsFalse();
            await Assert.That(result.Buttons[0].Extension).IsEqualTo("count-walls");
        }
    }

    [Test]
    public async Task Runner_ShowsNothingForAnEmptyResult()
    {
        using (Assert.Multiple())
        {
            await Assert.That(ButtonRunner.Describe(Empty.Value)).IsEqualTo("");
            await Assert.That(ButtonRunner.Describe(JsonDocument.Parse("{}").RootElement)).IsEqualTo("");
            await Assert.That(ButtonRunner.Describe(JsonDocument.Parse("\"7 walls\"").RootElement)).IsEqualTo("7 walls");
            await Assert.That(ButtonRunner.Describe(new { count = 7 })).Contains("7");
        }
    }

    /// <summary>The types of the add-in that load without Revit (the stubs lack what Application derives from).</summary>
    private static IEnumerable<Type> LoadableTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
