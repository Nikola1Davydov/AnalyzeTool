using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Serilog;
using System.Linq;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     Clean projects with dependencies.
    /// </summary>
    Target Clean => _ => _
        .OnlyWhenStatic(() => IsLocalBuild)
        .Executes(() =>
        {
            Project[] excludedProjects =
            [
                Solution._build,
                Solution.AnalyseTool_Test,
            ];

            CleanDirectory(ArtifactsDirectory);
            foreach (var project in Solution.AllProjects)
            {
                if (excludedProjects.Contains(project)) continue;

                CleanDirectory(project.Directory / "bin");
                CleanDirectory(project.Directory / "obj");
            }

            foreach (var configuration in GlobBuildConfigurations())
                DotNetClean(settings => settings
                    .SetProject(Solution.AnalyseTool)
                    .SetConfiguration(configuration)
                    .SetVerbosity(DotNetVerbosity.minimal)
                    .EnableNoLogo());
        });

    /// <summary>
    ///     Clean and log the specified directory.
    /// </summary>
    static void CleanDirectory(AbsolutePath path)
    {
        Log.Information("Cleaning directory: {Directory}", path);
        path.CreateOrCleanDirectory();
    }
}