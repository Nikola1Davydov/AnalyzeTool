using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Features.Extensions;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Text;

namespace AnalyseTool.Tests;

/// <summary>
/// The extension manager's state and its install / remove paths, on a temporary profile instead of the
/// user's %LOCALAPPDATA%: the disabled set, the registered folders and the authoring root, installing a
/// package (consent, replace, the dev-twin refusal), and uninstalling from both zones.
/// </summary>
[NotInParallel(TestKeys.ExtensionState)]
public class ExtensionProfileTests
{
    private const string Year = "2025";
    private string _profile = null!;
    private IDisposable _override = null!;

    [Before(Test)]
    public void UseTempProfile()
    {
        _profile = Path.Combine(Path.GetTempPath(), "at-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_profile);
        _override = PathProvider.UseProfile(_profile);
    }

    [After(Test)]
    public void RestoreProfile()
    {
        _override.Dispose();
        try { Directory.Delete(_profile, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Package(string id, string version)
    {
        string path = Path.Combine(_profile, $"{id}-{version}-{Guid.NewGuid():N}.zip");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using Stream s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(content));
        }
        Add($"{id}/plugin.json", $$"""{ "id": "{{id}}", "version": "{{version}}" }""");
        Add($"{id}/index.html", "<p>page</p>");
        return path;
    }

    private static JObject Install(string zip, bool consent = true, bool overwrite = false) =>
        JObject.FromObject(InstallExtensionFromFile.Install(
            new InstallExtensionFromFile.Request { Path = zip, Consent = consent, Overwrite = overwrite }, Year));

    private string WriteDev(string id)
    {
        string dir = Path.Combine(ExtensionSources.DefaultDevRoot, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""{ "id": "{{id}}", "version": "1.0.0" }""");
        return dir;
    }

    // --- extension state ----------------------------------------------------------------------------

    [Test]
    public async Task A_disabled_extension_stays_disabled_in_its_profile_only()
    {
        ExtensionStateStore.SetEnabled("acme.x", enabled: false);
        await Assert.That(ExtensionStateStore.IsEnabled("acme.x")).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(_profile, "extensions-state.json"))).Contains("acme.x");

        string other = Path.Combine(_profile, "other");
        using (PathProvider.UseProfile(other))
            await Assert.That(ExtensionStateStore.IsEnabled("acme.x")).IsTrue();

        // Back on the first profile the set is read from its file again.
        await Assert.That(ExtensionStateStore.IsEnabled("ACME.X")).IsFalse();
    }

    // --- sources and the authoring root --------------------------------------------------------------

    [Test]
    public async Task Registered_folders_and_the_authoring_root_follow_the_rules()
    {
        string team = Path.Combine(_profile, "team");
        Directory.CreateDirectory(team);

        ExtensionSources.AddRoot(team);
        ExtensionSources.AddRoot(team); // twice: still one

        using (Assert.Multiple())
        {
            await Assert.That(ExtensionSources.UserRoots()).HasCount(1);
            await Assert.That(ExtensionSources.AllRoots().Select(r => r.Path)).Contains(ExtensionSources.DefaultManagedRoot);
            await Assert.That(ExtensionSources.AuthoringRoot).IsEqualTo(ExtensionSources.DefaultDevRoot);
            await Assert.That(ExtensionSources.SetAuthoringRoot(ExtensionSources.DefaultManagedRoot)).IsNotNull(); // managed: refused
            await Assert.That(ExtensionSources.SetAuthoringRoot(Path.Combine(_profile, "nowhere"))).IsNotNull(); // unregistered
            await Assert.That(ExtensionSources.SetAuthoringRoot(team)).IsNull();
            await Assert.That(ExtensionSources.AuthoringRoot).IsEqualTo(Path.GetFullPath(team));
        }

        // A folder that is no longer registered stops receiving saves: back to the default.
        ExtensionSources.RemoveRoot(team);
        await Assert.That(ExtensionSources.AuthoringRoot).IsEqualTo(ExtensionSources.DefaultDevRoot);
    }

    // --- install ------------------------------------------------------------------------------------

    [Test]
    public async Task A_package_installs_into_the_managed_root_only_with_consent()
    {
        string zip = Package("acme.walls", "1.0.0");

        await Assert.That(() => Install(zip, consent: false)).Throws<InvalidOperationException>();

        JObject result = Install(zip);
        string target = Path.Combine(ExtensionSources.DefaultManagedRoot, "acme.walls");

        using (Assert.Multiple())
        {
            await Assert.That((bool)result["installed"]!).IsTrue();
            await Assert.That(File.Exists(Path.Combine(target, "plugin.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(target, "index.html"))).IsTrue();
        }
    }

    [Test]
    public async Task Installing_again_asks_first_and_a_replace_leaves_no_work_folders()
    {
        Install(Package("acme.walls", "1.0.0"));

        JObject again = Install(Package("acme.walls", "1.1.0"));
        await Assert.That((bool)again["alreadyInstalled"]!).IsTrue();

        JObject replaced = Install(Package("acme.walls", "1.1.0"), overwrite: true);
        string root = ExtensionSources.DefaultManagedRoot;

        using (Assert.Multiple())
        {
            await Assert.That((bool)replaced["replaced"]!).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(root, "acme.walls", "plugin.json"))).Contains("1.1.0");
            await Assert.That(Directory.GetDirectories(root).Select(Path.GetFileName)).IsEquivalentTo(new[] { "acme.walls" });
        }
    }

    [Test]
    public async Task A_package_whose_id_exists_as_a_dev_extension_is_refused()
    {
        WriteDev("acme.twin");

        await Assert.That(() => Install(Package("acme.twin", "1.0.0"))).Throws<InvalidOperationException>()
            .WithMessageContaining("dev extension");
    }

    // --- remove -------------------------------------------------------------------------------------

    [Test]
    public async Task Uninstalling_removes_the_folder_and_forgets_the_disabled_flag()
    {
        Install(Package("acme.walls", "1.0.0"));
        ExtensionStateStore.SetEnabled("acme.walls", enabled: false);

        RemoveExtension.Remove("acme.walls", Year);

        using (Assert.Multiple())
        {
            await Assert.That(Directory.GetDirectories(ExtensionSources.DefaultManagedRoot)).IsEmpty();
            await Assert.That(ExtensionStateStore.IsEnabled("acme.walls")).IsTrue();
        }
    }

    [Test]
    public async Task Uninstall_touches_managed_packages_only()
    {
        WriteDev("acme.mine");

        using (Assert.Multiple())
        {
            await Assert.That(() => RemoveExtension.Remove("acme.mine", Year)).Throws<InvalidOperationException>()
                .WithMessageContaining("dev extension");
            await Assert.That(() => RemoveExtension.Remove("acme.nobody", Year)).Throws<InvalidOperationException>();
            await Assert.That(() => RemoveExtension.Remove("  ", Year)).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task A_dev_extension_is_deleted_unless_it_is_a_git_working_folder()
    {
        string plain = WriteDev("acme.plain");
        string repo = WriteDev("acme.repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Install(Package("acme.walls", "1.0.0"));

        RemoveDevExtension.Remove("acme.plain", Year);

        using (Assert.Multiple())
        {
            await Assert.That(Directory.Exists(plain)).IsFalse();
            await Assert.That(() => RemoveDevExtension.Remove("acme.repo", Year)).Throws<InvalidOperationException>()
                .WithMessageContaining("git");
            await Assert.That(Directory.Exists(repo)).IsTrue();
            await Assert.That(() => RemoveDevExtension.Remove("acme.walls", Year)).Throws<InvalidOperationException>()
                .WithMessageContaining("installed package");
        }
    }
}
