using Nuke.Common;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     Create the .msi installers.
    /// </summary>
    Target CreateInstaller => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            const string configuration = "Release";
            foreach ((Nuke.Common.ProjectModel.Project wixInstaller, Nuke.Common.ProjectModel.Project wixTarget) in InstallersMap)
            {
                Log.Information("Project: {Name}", wixTarget.Name);

                DotNetBuild(settings => settings
                    .SetProjectFile(wixInstaller)
                    .SetConfiguration(configuration)
                    .SetVersion(ReleaseVersionNumber)
                    .SetVerbosity(DotNetVerbosity.minimal));

                string builderFile = Directory
                    .EnumerateFiles(wixInstaller.Directory / "bin" / configuration, $"{wixInstaller.Name}.exe")
                    .FirstOrDefault()
                    .NotNull($"No installer builder was found for the project: {wixInstaller.Name}");

                string[] targetDirectories = Directory.GetDirectories(wixTarget.Directory, $"* {configuration} *", SearchOption.AllDirectories);
                Assert.NotEmpty(targetDirectories, "No content were found to create an installer");

                string arguments = targetDirectories.Select(path => path.DoubleQuoteIfNeeded()).JoinSpace();
                IProcess process = ProcessTasks.StartProcess(builderFile, arguments, logInvocation: false);
                process.AssertZeroExitCode();
            }
        });
}