using ModularPipelines.FileSystem;
using Shouldly;
using Sourcy.DotNet;
using File = ModularPipelines.FileSystem.File;

namespace Build.Modules;

/// <summary>
///     The publish folders of the Launcher — what the installer and the bundle ship, one per Revit version.
/// </summary>
internal static class AddinPublish
{
    /// <summary>The project whose publish folders hold the add-in: the Launcher, with the .addin and the add-in next to it.</summary>
    public static File Project { get; } = new(Projects.AnalyseTool_Launcher.FullName);

    /// <summary>
    ///     Finds <c>bin\Release.R2x\publish</c> of the Launcher and checks that each one ships the ClientApp pages.
    /// </summary>
    /// <remarks>
    ///     Release only: a Debug build publishes too (it deploys to the Revit add-ins folder from there). Without
    ///     <c>ClientApp\dist</c> the add-in opens empty windows, so such a build must not reach a user.
    /// </remarks>
    public static Folder[] GetFolders()
    {
        var folders = Project.Folder!
            .GetFolder("bin")
            .GetFolders(folder => folder.Name == "publish" && folder.Parent!.Name.StartsWith("Release.R", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        folders.ShouldNotBeEmpty("No Release publish folders of the Launcher were found");

        foreach (var folder in folders)
        {
            var pages = folder.GetFolder(Project.NameWithoutExtension).GetFolder("ClientApp").GetFolder("dist").GetFile("index.html");
            pages.Exists.ShouldBeTrue($"The ClientApp pages are missing in {folder.Path}: build ClientApp first (npm run build)");
        }

        return folders;
    }
}
