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

            Log.Information("Project: {Name}", Solution.AnalyseTool.Name);

            DotNetBuild(settings => settings
                .SetProjectFile(Solution.Installer)
                .SetConfiguration(configuration)
                .SetVersion(ReleaseVersionNumber)
                .SetVerbosity(DotNetVerbosity.minimal));
        });
}