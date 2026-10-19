using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions;

namespace AnalyseTool.Tests;

/// <summary>
/// The extension loader on real folders in a temp root, without Revit: a host-built extension compiled
/// from src\ and registered, a broken one reported without stopping the others, and a reload that drops
/// what is gone. Until the IRevitTaskHub seam and the loader's root parameter, none of this could run
/// outside Revit.
/// Not in parallel with each other: a reload clears the process-wide ExtensionDiagnostics, so one test's
/// reload could wipe the error another had just asserted on (in Revit reloads are serialized).
/// </summary>
[NotInParallel(TestKeys.ExtensionState)]
public class ExtensionLoaderTests
{
    private const string Year = "2025";
    private string _root = null!;
    private string _suffix = null!;

    [Before(Test)]
    public void MakeRoot()
    {
        _suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        _root = Path.Combine(Path.GetTempPath(), "at-loader-" + _suffix);
        Directory.CreateDirectory(_root);
    }

    [After(Test)]
    public void RemoveRoot()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Ids unique per test: extension state and diagnostics are process-wide.
    private string Id(string name) => $"acme.{name}{_suffix}";

    private (CommandDispatcher Dispatcher, ExtensionLoader Loader) NewLoader()
    {
        CommandDispatcher dispatcher = new(null!);
        ExtensionLoader loader = new(dispatcher, Year,
            () => new[] { new ExtensionSourceRoot(_root, ExtensionZone.Dev, IsDefault: false) });
        return (dispatcher, loader);
    }

    private string WriteHostBuilt(string id)
    {
        string dir = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""{ "id": "{{id}}", "version": "1.0.0" }""");
        File.WriteAllText(Path.Combine(dir, "src", "Ping.cs"), """
            using System.Threading;
            using System.Threading.Tasks;
            using AnalyseTool.Sdk;

            [RevitCommand(Description = "Answers pong.")]
            public sealed class Ping : IRevitTask
            {
                public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) => Task.FromResult<object?>("pong");
            }
            """);
        return dir;
    }

    [Test]
    public async Task A_host_built_extension_is_compiled_and_its_command_registered()
    {
        string id = Id("good");
        string dir = WriteHostBuilt(id);
        (CommandDispatcher dispatcher, ExtensionLoader loader) = NewLoader();

        loader.Reload();

        using (Assert.Multiple())
        {
            await Assert.That(ExtensionDiagnostics.GetError(id)).IsNull();
            await Assert.That(dispatcher.IsRegistered($"{id}.Ping")).IsTrue();
            await Assert.That(File.Exists(Path.Combine(dir, Year, id + ".dll"))).IsTrue();
        }
    }

    [Test]
    public async Task A_broken_extension_is_reported_and_the_others_still_load()
    {
        string good = Id("good");
        string broken = Id("broken");
        WriteHostBuilt(good);
        string brokenDir = Path.Combine(_root, broken);
        Directory.CreateDirectory(Path.Combine(brokenDir, Year));
        File.WriteAllText(Path.Combine(brokenDir, "plugin.json"),
            $$"""{ "id": "{{broken}}", "version": "1.0.0", "entryAssembly": "{{broken}}.dll" }""");
        File.WriteAllBytes(Path.Combine(brokenDir, Year, broken + ".dll"), new byte[] { 1, 2, 3, 4 }); // not an assembly
        (CommandDispatcher dispatcher, ExtensionLoader loader) = NewLoader();

        loader.Reload();

        using (Assert.Multiple())
        {
            await Assert.That(ExtensionDiagnostics.GetError(broken)).IsNotNull();
            await Assert.That(dispatcher.IsRegistered($"{good}.Ping")).IsTrue();
        }
    }

    [Test]
    public async Task A_reload_drops_the_commands_of_an_extension_that_is_gone()
    {
        string id = Id("gone");
        string dir = WriteHostBuilt(id);
        (CommandDispatcher dispatcher, ExtensionLoader loader) = NewLoader();
        loader.Reload();
        await Assert.That(dispatcher.IsRegistered($"{id}.Ping")).IsTrue();

        Directory.Delete(dir, recursive: true);
        loader.Reload();

        await Assert.That(dispatcher.IsRegistered($"{id}.Ping")).IsFalse();
    }

    [Test]
    public async Task Sources_that_do_not_compile_are_reported()
    {
        string id = Id("bad");
        string dir = WriteHostBuilt(id);
        File.WriteAllText(Path.Combine(dir, "src", "Ping.cs"), "this is not C#");
        (CommandDispatcher dispatcher, ExtensionLoader loader) = NewLoader();

        loader.Reload();

        using (Assert.Multiple())
        {
            await Assert.That(ExtensionDiagnostics.GetError(id)).IsNotNull();
            await Assert.That(dispatcher.IsRegistered($"{id}.Ping")).IsFalse();
        }
    }
}
