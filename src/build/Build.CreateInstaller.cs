using Nuke.Common;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     Create the .msi installers.
    /// </summary>
    Target CreateInstaller => _ => _
        .DependsOn(BuildLauncher)
        .Executes(() =>
        {
            const string configuration = "Release";

            Log.Information("Project: {Name}", Solution.AnalyseTool.Name);

            DotNetBuild(settings => settings
                .SetProjectFile(Solution.Installer)
                .SetConfiguration(configuration)
                .SetVersion(ReleaseVersionNumber)
                .SetVerbosity(DotNetVerbosity.minimal));
        });
}