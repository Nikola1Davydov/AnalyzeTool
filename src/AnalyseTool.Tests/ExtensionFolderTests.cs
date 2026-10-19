using AnalyseTool.Core.Common.Extensions;

namespace AnalyseTool.Tests;

/// <summary>Paths an extension's own files name (its icon, its id as a folder) stay inside the
/// extension. The icon path used to be combined as-is, so a plugin.json could make the MCP-exposed
/// listing read any file on disk.</summary>
public class ExtensionFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "at-ext", "Acme.Sample");

    [Test]
    [Arguments("icon.png")]
    [Arguments("assets/icon.png")]
    [Arguments(@"assets\..\icon.png")]
    public async Task A_path_inside_the_extension_resolves(string relative)
    {
        string? path = ExtensionFolder.ResolveInside(Root, relative);
        await Assert.That(path).IsNotNull();
        await Assert.That(Path.GetFullPath(path!)).StartsWith(Root + Path.DirectorySeparatorChar);
    }

    [Test]
    [Arguments(@"..\..\secret.txt")]
    [Arguments("../Other.Extension/icon.png")]
    [Arguments(@"C:\Windows\win.ini")]
    [Arguments("..")]
    public async Task A_path_that_leaves_the_extension_is_refused(string relative)
    {
        await Assert.That(ExtensionFolder.ResolveInside(Root, relative)).IsNull();
    }

    [Test]
    public async Task A_sibling_whose_name_starts_like_the_root_is_refused()
    {
        // "Acme.Sample2" starts with "Acme.Sample" — the separator in the comparison is what stops it.
        await Assert.That(ExtensionFolder.ResolveInside(Root, @"..\Acme.Sample2\icon.png")).IsNull();
    }
}
