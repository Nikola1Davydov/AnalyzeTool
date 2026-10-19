using AnalyseTool.Core.Common.Extensions;
using System.IO.Compression;
using System.Text;

namespace AnalyseTool.Tests;

/// <summary>The package contract both sides share (pack and install): where plugin.json may sit, what a
/// valid manifest needs, and that nothing in a zip extracts outside the target folder.</summary>
public class ExtensionPackageTests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Zip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".zip");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            using Stream s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(content));
        }
        return path;
    }

    private const string Manifest = """{ "id": "acme.walls", "version": "1.2.0", "entryAssembly": "Acme.Walls.dll" }""";

    [Test]
    public async Task A_package_in_one_top_folder_validates_with_its_years()
    {
        string zip = Zip(("acme.walls/plugin.json", Manifest), ("acme.walls/2025/Acme.Walls.dll", "x"), ("acme.walls/2027/Acme.Walls.dll", "x"));

        ExtensionPackageInfo info = ExtensionPackage.Validate(zip);

        using (Assert.Multiple())
        {
            await Assert.That(info.Manifest.Id).IsEqualTo("acme.walls");
            await Assert.That(info.Prefix).IsEqualTo("acme.walls/");
            await Assert.That(info.BinaryYears).IsEquivalentTo(new[] { "2025", "2027" });
        }
    }

    [Test]
    public async Task Backslash_entry_names_from_Compress_Archive_are_understood()
    {
        string zip = Zip(("plugin.json", Manifest), ("2025\\Acme.Walls.dll", "x"));

        ExtensionPackageInfo info = ExtensionPackage.Validate(zip);

        await Assert.That(info.BinaryYears).IsEquivalentTo(new[] { "2025" });
    }

    [Test]
    [Arguments("../evil.dll")]
    [Arguments("acme.walls/../../evil.dll")]
    public async Task An_entry_that_escapes_the_folder_is_refused(string evil)
    {
        string zip = Zip(("plugin.json", Manifest), ("2025/Acme.Walls.dll", "x"), (evil, "x"));

        await Assert.That(() => ExtensionPackage.Validate(zip)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("""{ "version": "1.0.0" }""")]
    [Arguments("""{ "id": "acme walls!", "version": "1.0.0" }""")]
    [Arguments("""{ "id": "acme.walls" }""")]
    [Arguments("""{ "id": "acme.walls", "version": "1.0.0", "entryAssembly": "Missing.dll" }""")]
    public async Task An_incomplete_manifest_is_refused(string manifest)
    {
        string zip = Zip(("plugin.json", manifest));

        await Assert.That(() => ExtensionPackage.Validate(zip)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Extraction_strips_the_top_folder()
    {
        string zip = Zip(("acme.walls/plugin.json", Manifest), ("acme.walls/2025/Acme.Walls.dll", "dll"));
        string target = Path.Combine(_dir, "out");

        ExtensionPackage.ExtractTo(zip, ExtensionPackage.Validate(zip), target);

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(Path.Combine(target, "plugin.json"))).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(target, "2025", "Acme.Walls.dll"))).IsEqualTo("dll");
        }
    }
}
