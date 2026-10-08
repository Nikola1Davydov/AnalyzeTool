using AnalyseTool.Core.Common.Extensions;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests;

/// <summary>
/// The file rules of host-built extensions: which folders the host builds, how a script folder of the
/// old format becomes one, when a build is due, and which folders the authoring commands may write.
/// The compile itself needs the Revit API and is covered in Revit (ScriptCompileTests).
/// </summary>
public class HostBuildTests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public async Task A_script_folder_moves_into_src_and_gains_an_entry_assembly()
    {
        File.WriteAllText(Path.Combine(_dir, "plugin.json"),
            """{"id":"acme.walls","version":"1.0.0","ui":{"button":{"name":"Walls","command":"acme.walls.CountWalls"}}}""");
        File.WriteAllText(Path.Combine(_dir, "Command.cs"), "// body");
        File.WriteAllText(Path.Combine(_dir, "Helpers.cs"), "// helpers");

        await Assert.That(HostBuild.IsScriptFolder(_dir, Manifest())).IsTrue();

        string? error = HostBuild.MigrateScriptFolder(_dir, "acme.walls");

        await Assert.That(error).IsNull();
        await Assert.That(Directory.GetFiles(_dir, "*.cs").Length).IsEqualTo(0);
        await Assert.That(HostBuild.SourceFiles(_dir).Select(f => Path.GetFileName(f)))
            .IsEquivalentTo(new[] { "Command.cs", "Helpers.cs" });
        JObject m = JObject.Parse(File.ReadAllText(Path.Combine(_dir, "plugin.json")));
        await Assert.That((string?)m["entryAssembly"]).IsEqualTo("acme.walls.dll");
        // The button still names the same command: migration changes how it is built, not what it is.
        await Assert.That((string?)m["ui"]?["button"]?["command"]).IsEqualTo("acme.walls.CountWalls");
        await Assert.That(HostBuild.IsHostBuilt(_dir)).IsTrue();
        await Assert.That(HostBuild.IsScriptFolder(_dir, Manifest())).IsFalse();
    }

    [Test]
    public async Task A_project_the_author_builds_is_never_host_built_nor_migrated()
    {
        File.WriteAllText(Path.Combine(_dir, "plugin.json"), """{"id":"acme.proj","version":"1.0.0"}""");
        File.WriteAllText(Path.Combine(_dir, "Acme.Proj.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_dir, "Hello.cs"), "// source");
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "src", "More.cs"), "// source");

        await Assert.That(HostBuild.IsScriptFolder(_dir, Manifest())).IsFalse();
        await Assert.That(HostBuild.IsHostBuilt(_dir)).IsFalse();
    }

    [Test]
    public async Task A_build_is_due_when_missing_or_older_than_a_source()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        string source = Path.Combine(_dir, "src", "Command.cs");
        File.WriteAllText(source, "// v1");

        await Assert.That(HostBuild.NeedsBuild(_dir, "acme.dll", "2025")).IsTrue();

        Directory.CreateDirectory(Path.Combine(_dir, "2025"));
        string dll = Path.Combine(_dir, "2025", "acme.dll");
        File.WriteAllBytes(dll, new byte[] { 1 });
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddMinutes(-1));
        await Assert.That(HostBuild.NeedsBuild(_dir, "acme.dll", "2025")).IsFalse();

        // Another Revit year has no build of its own yet.
        await Assert.That(HostBuild.NeedsBuild(_dir, "acme.dll", "2027")).IsTrue();

        // A hand edit after the build makes it stale.
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow);
        await Assert.That(HostBuild.NeedsBuild(_dir, "acme.dll", "2025")).IsTrue();
    }

    [Test]
    public async Task The_host_build_layout_counts_as_a_generated_folder()
    {
        File.WriteAllText(Path.Combine(_dir, "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<p/>");
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "src", "Command.cs"), "// source");
        Directory.CreateDirectory(Path.Combine(_dir, "2025"));
        File.WriteAllBytes(Path.Combine(_dir, "2025", "acme.dll"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_dir, "2025", "acme.pdb"), new byte[] { 1 });

        await Assert.That(ExtensionFolder.IsGeneratedFolder(_dir)).IsTrue();
    }

    [Test]
    public async Task Anything_else_in_the_folder_makes_it_hands_off()
    {
        File.WriteAllText(Path.Combine(_dir, "plugin.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "src", "Command.cs"), "// source");

        File.WriteAllText(Path.Combine(_dir, "src", "notes.json"), "{}");
        await Assert.That(ExtensionFolder.IsGeneratedFolder(_dir)).IsFalse();
        File.Delete(Path.Combine(_dir, "src", "notes.json"));

        Directory.CreateDirectory(Path.Combine(_dir, "bin"));
        await Assert.That(ExtensionFolder.IsGeneratedFolder(_dir)).IsFalse();
        Directory.Delete(Path.Combine(_dir, "bin"));

        File.WriteAllBytes(Path.Combine(_dir, "acme.dll"), new byte[] { 1 });
        await Assert.That(ExtensionFolder.IsGeneratedFolder(_dir)).IsFalse();
    }

    private ExtensionManifest Manifest() =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<ExtensionManifest>(
            File.ReadAllText(Path.Combine(_dir, "plugin.json")))!;
}
