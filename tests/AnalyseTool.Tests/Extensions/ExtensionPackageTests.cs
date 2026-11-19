using System.IO;
using System.IO.Compression;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Packages;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Tests.Extensions;

/// <summary>Packages of 1.x install unchanged; every install is a version of the folder.</summary>
[NotInParallel("extensions")]
public sealed class ExtensionPackageTests
{
    [Test]
    public async Task Install_SourcePackage_LoadsAndKeepsAVersionPerInstall()
    {
        using var environment = new ExtensionTestEnvironment();
        var first = Zip(environment, "1.0.0", "1");
        var second = Zip(environment, "1.1.0", "2");

        var installed = await environment.Manager.InstallPackageAsync(first, false, CancellationToken.None);
        var again = await environment.Manager.InstallPackageAsync(second, false, CancellationToken.None);
        var updated = await environment.Manager.InstallPackageAsync(second, true, CancellationToken.None);

        var history = environment.History.GetHistory(updated.Directory);
        using (Assert.Multiple())
        {
            await Assert.That(installed.Extension!.Zone).IsEqualTo(ExtensionZone.Managed);
            await Assert.That(installed.Extension.Kind).IsEqualTo(ExtensionKind.Source);
            await Assert.That(installed.Extension.Commands).Contains("pkg.Echo");
            await Assert.That(again.AlreadyInstalled).IsTrue();
            await Assert.That(updated.Replaced).IsTrue();
            await Assert.That(history.Count).IsEqualTo(2);
            await Assert.That(history[0].Message).StartsWith("Installed 1.1.0");
        }
    }

    [Test]
    public async Task Validate_PathLeavingTheFolder_IsRefused()
    {
        using var environment = new ExtensionTestEnvironment();
        var path = Path.Combine(environment.Root, "evil.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "plugin.json", """{ "id": "evil", "version": "1.0.0" }""");
            Write(zip, "../outside.txt", "x");
        }

        await Assert.That(() => ExtensionPackage.Validate(path)).Throws<CommandException>();
    }

    [Test]
    public async Task Validate_ManifestInOneTopFolder_IsFound()
    {
        using var environment = new ExtensionTestEnvironment();
        var path = Path.Combine(environment.Root, "nested.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "acme/plugin.json", """{ "id": "acme", "version": "1.0.0", "entryAssembly": "Acme.dll" }""");
            Write(zip, "acme/2025/Acme.dll", "");
        }

        var info = ExtensionPackage.Validate(path);

        await Assert.That(info.Prefix).IsEqualTo("acme/");
        await Assert.That(info.BinaryYears).IsEquivalentTo(["2025"]);
    }

    private static string Zip(ExtensionTestEnvironment environment, string version, string answer)
    {
        var path = Path.Combine(environment.Root, $"pkg-{version}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, "plugin.json", $$"""{ "id": "pkg", "version": "{{version}}", "build": "host" }""");
        Write(zip, "src/Echo.cs", ExtensionTestEnvironment.EchoCommand(answer: answer));
        return path;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
