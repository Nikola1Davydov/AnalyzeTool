using AnalyseTool.Core.Common.Extensions;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests;

/// <summary>
/// The writer MERGES into plugin.json rather than replacing it: several commands build one extension
/// between them, and a manifest carries fields none of them know. Every rule here was once a way to
/// lose a field silently.
/// </summary>
public class ManifestWriterTests
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
    public async Task Unknown_keys_and_untouched_fields_survive_a_write()
    {
        Seed("""{"id":"acme.x","version":"2.3.4","publisher":"Acme","icon":"icon.png","futureField":{"deep":true},"ui":{"entryHtml":"index.html","devUrl":"http://localhost:5173","button":{"name":"X","tooltip":"t"}}}""");

        ExtensionManifestWriter.Write(_dir, "acme.x", new ManifestEdit { ButtonName = "Renamed" });

        JObject m = Read();
        await Assert.That((string?)m["version"]).IsEqualTo("2.3.4");
        await Assert.That((string?)m["publisher"]).IsEqualTo("Acme");
        await Assert.That((string?)m["icon"]).IsEqualTo("icon.png");
        await Assert.That((bool?)m["futureField"]?["deep"]).IsEqualTo(true);
        await Assert.That((string?)m["ui"]?["devUrl"]).IsEqualTo("http://localhost:5173");
        await Assert.That((string?)m["ui"]?["button"]?["tooltip"]).IsEqualTo("t");
        await Assert.That((string?)m["ui"]?["button"]?["name"]).IsEqualTo("Renamed");
    }

    [Test]
    public async Task Empty_string_removes_a_vendor_field_and_null_leaves_it()
    {
        Seed("""{"id":"acme.x","version":"1.0.0","description":"old","website":"https://a.example"}""");

        ExtensionManifestWriter.Write(_dir, "acme.x", new ManifestEdit { Description = "", Website = null, Publisher = "New" });

        JObject m = Read();
        await Assert.That(m["description"]).IsNull();
        await Assert.That((string?)m["website"]).IsEqualTo("https://a.example");
        await Assert.That((string?)m["publisher"]).IsEqualTo("New");
    }

    [Test]
    public async Task Push_and_order_zero_are_written_as_absence()
    {
        Seed("""{"id":"acme.x","version":"1.0.0","ui":{"button":{"name":"X","kind":"stacked","order":4}}}""");

        ExtensionManifestWriter.Write(_dir, "acme.x", new ManifestEdit { Kind = "push", Order = 0 });

        JObject button = (JObject)Read()["ui"]!["button"]!;
        await Assert.That(button["kind"]).IsNull();
        await Assert.That(button["order"]).IsNull();
    }

    [Test]
    public async Task A_page_clears_the_command_and_a_nameless_button_is_not_written()
    {
        Seed("""{"id":"acme.x","version":"1.0.0","ui":{"button":{"name":"X","command":"acme.x.Run"}}}""");
        ExtensionManifestWriter.Write(_dir, "acme.x", new ManifestEdit { EntryHtml = "index.html" });
        await Assert.That(Read()["ui"]!["button"]!["command"]).IsNull();

        Seed("""{"id":"acme.y","version":"1.0.0"}""");
        ExtensionManifestWriter.Write(_dir, "acme.y", new ManifestEdit { Tooltip = "only a tooltip" });
        // No name means no button — writing {} would put a nameless entry on the ribbon.
        await Assert.That(Read()["ui"]?["button"]).IsNull();
    }

    [Test]
    public async Task An_extension_without_ui_gets_no_empty_ui_object()
    {
        Seed("""{"id":"acme.x","version":"1.0.0"}""");
        ExtensionManifestWriter.Write(_dir, "acme.x", new ManifestEdit { Description = "d" });
        await Assert.That(Read()["ui"]).IsNull();
    }

    private void Seed(string json) => File.WriteAllText(Path.Combine(_dir, "plugin.json"), json);
    private JObject Read() => JObject.Parse(File.ReadAllText(Path.Combine(_dir, "plugin.json")));
}
