using System.IO;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;

namespace AnalyseTool.Tests.Extensions;

/// <summary>The folders of 1.x read unchanged; the kind of an extension decided from its files.</summary>
[NotInParallel("extensions")]
public sealed class ExtensionCatalogTests
{
    [Test]
    public async Task Scan_DllExtensionOf1x_ResolvesTheBuildOfTheRunningYearFirst()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "acme")).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "acme.sample", "version": "1.0.0", "entryAssembly": "Acme.dll", "ui": { "button": { "name": "Hello" } } }""");
        Directory.CreateDirectory(Path.Combine(folder, "2025"));
        File.WriteAllText(Path.Combine(folder, "2025", "Acme.dll"), "");
        File.WriteAllText(Path.Combine(folder, "Acme.dll"), "");

        var descriptor = environment.Catalog.Scan().Single();

        using (Assert.Multiple())
        {
            await Assert.That(descriptor.Kind).IsEqualTo(ExtensionKind.Dll);
            await Assert.That(descriptor.EntryAssemblyPath).IsEqualTo(Path.Combine(folder, "2025", "Acme.dll"));
            await Assert.That(descriptor.BinaryYears).IsEquivalentTo(["2025"]);
            await Assert.That(descriptor.Manifest.Ui!.EffectiveButtons().Single().Name).IsEqualTo("Hello");
        }
    }

    [Test]
    public async Task Scan_SourcesWithoutProject_AreBuiltByTheHost()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "saved", "src")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "saved", "entryAssembly": "saved.dll" }""");
        File.WriteAllText(Path.Combine(folder, "src", "A.cs"), "class A {}");

        await Assert.That(environment.Catalog.Scan().Single().Kind).IsEqualTo(ExtensionKind.Source);
    }

    [Test]
    public async Task Scan_SourcesWithAProject_AreTheAuthorsDll()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "own", "src")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "own", "entryAssembly": "Own.dll" }""");
        File.WriteAllText(Path.Combine(folder, "src", "A.cs"), "class A {}");
        File.WriteAllText(Path.Combine(folder, "Own.csproj"), "<Project/>");

        await Assert.That(environment.Catalog.Scan().Single().Kind).IsEqualTo(ExtensionKind.Dll);
    }

    [Test]
    public async Task Scan_BuildHost_WinsOverAnEditorProject()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "mine", "src")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "mine", "build": "host" }""");
        File.WriteAllText(Path.Combine(folder, "src", "A.cs"), "class A {}");
        Directory.CreateDirectory(Path.Combine(folder, ".editor"));
        File.WriteAllText(Path.Combine(folder, ".editor", "mine.csproj"), "<Project/>");

        await Assert.That(environment.Catalog.Scan().Single().Kind).IsEqualTo(ExtensionKind.Source);
    }

    [Test]
    public async Task Scan_LegacyYearLayout_OnlyTheRunningYear()
    {
        using var environment = new ExtensionTestEnvironment();
        foreach (var year in new[] { "2025", "2026" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, year, "old")).FullName;
            File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""{ "id": "old{{year}}" }""");
        }

        var found = environment.Catalog.Scan();

        await Assert.That(found.Select(descriptor => descriptor.Id)).IsEquivalentTo(["old2025"]);
        await Assert.That(found.Single().IsLegacyLayout).IsTrue();
    }

    [Test]
    public async Task Scan_BrokenManifest_IsReportedNotThrown()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "broken")).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), "{ not json");
        var problems = new Dictionary<string, string>();

        var found = environment.Catalog.Scan(problems);

        await Assert.That(found).IsEmpty();
        await Assert.That(problems.Keys).Contains(folder);
    }

    [Test]
    public async Task UpdateManifest_KeepsFieldsItDoesNotKnow()
    {
        using var environment = new ExtensionTestEnvironment();
        var folder = Directory.CreateDirectory(Path.Combine(environment.Paths.DevRoot, "x")).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "x", "future": { "a": 1 } }""");

        ExtensionManifest.Update(folder, node => node["version"] = "2.0.0");

        var text = File.ReadAllText(Path.Combine(folder, "plugin.json"));
        await Assert.That(text).Contains("\"future\"");
        await Assert.That(ExtensionManifest.Read(folder).Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Pinned_And_Disabled_SurviveInTheStateFile()
    {
        using var environment = new ExtensionTestEnvironment();
        environment.Store.SetEnabled("a", false);
        environment.Store.SetPinnedVersion("b", "abc1234");

        var reread = new AnalyseTool.Extensions.Storage.ExtensionStateStore(environment.Paths);

        await Assert.That(reread.IsEnabled("A")).IsFalse();
        await Assert.That(reread.GetPinnedVersion("b")).IsEqualTo("abc1234");
    }
}
