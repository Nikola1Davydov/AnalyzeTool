using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using System.Configuration;
using System.IO;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     Compile all solution configurations.
    /// </summary>
    Target BuildClientApp => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            foreach (string configuration in GlobBuildConfigurations())
            {
                DotNetBuild(settings => settings
                    .SetProjectFile(Solution.clientapp)
                    .SetConfiguration(configuration)
                    .SetVersion(ReleaseVersionNumber)
                    .SetVerbosity(DotNetVerbosity.minimal));


            }
        });

    Target BuildLauncher => _ => _
        .DependsOn(BuildClientApp)
        .Executes(() =>
        {
            foreach (string configuration in GlobBuildConfigurations())
            {
                DotNetBuild(settings => settings
                    .SetProjectFile(Solution.AnalyseTool_Launcher)
                    .SetConfiguration(configuration)
                    .SetVersion(ReleaseVersionNumber)
                    .SetVerbosity(DotNetVerbosity.minimal));

                CopyClientAppDistToLauncher(configuration);
            }
        });

    void CopyClientAppDistToLauncher(string configuration)
    {
        var clientAppDistPath = Solution.clientapp.Directory / "dist";
        var launcherOutputPath = Solution.AnalyseTool_Launcher.Directory / "bin" / configuration / "AnalyseTool";

        if (!Directory.Exists(clientAppDistPath))
        {
            Serilog.Log.Warning($"ClientApp dist folder not found: {clientAppDistPath}");
            return;
        }

        Serilog.Log.Information($"Copying clientapp/dist to {launcherOutputPath}");

        // Копируем все файлы и папки рекурсивно
        CopyDirectoryRecursively(clientAppDistPath, launcherOutputPath);

        Serilog.Log.Information($"Successfully copied clientapp/dist to Launcher output");
    }

    void CopyDirectoryRecursively(AbsolutePath source, AbsolutePath destination)
    {
        Directory.CreateDirectory(destination);

        // Копируем все файлы
        foreach (var file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var destFile = destination / relativePath;

            Directory.CreateDirectory(Path.GetDirectoryName(destFile));
            File.Copy(file, destFile, overwrite: true);
        }
    }
}