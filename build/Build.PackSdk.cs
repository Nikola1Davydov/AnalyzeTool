using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     nuget.org API key (CI secret). When absent, <see cref="PushNuGet"/> is skipped — the .nupkg
    ///     is still produced and attached to the GitHub release.
    /// </summary>
    [Parameter] [Secret] readonly string NuGetApiKey;

    /// <summary>
    ///     Packs the AnalyseTool.Sdk NuGet into the artifacts directory. Uses the FULL release version
    ///     (e.g. <c>1.4.0-beta.1</c>) so a beta tag yields a NuGet prerelease, unlike the installer which
    ///     uses the numeric-only version.
    /// </summary>
    Target PackSdk => _ => _
        .Executes(() =>
        {
            Log.Information("Packing {Name} {Version}", Solution.AnalyseTool_Sdk.Name, ReleaseVersion);

            DotNetPack(settings => settings
                .SetProject(Solution.AnalyseTool_Sdk)
                .SetConfiguration("Release R25")
                .SetVersion(ReleaseVersion)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetVerbosity(DotNetVerbosity.minimal));
        });

    /// <summary>
    ///     Pushes the packed SDK to nuget.org. Runs only on CI and only when an API key is configured;
    ///     otherwise it is skipped (the package is still available as a GitHub release asset).
    /// </summary>
    Target PushNuGet => _ => _
        .DependsOn(PackSdk)
        .OnlyWhenStatic(() => IsServerBuild)
        .OnlyWhenDynamic(() => !string.IsNullOrWhiteSpace(NuGetApiKey))
        .Executes(() =>
        {
            foreach (AbsolutePath package in ArtifactsDirectory.GlobFiles("*.nupkg"))
            {
                Log.Information("Pushing {Package} to nuget.org", package);

                DotNetNuGetPush(settings => settings
                    .SetTargetPath(package)
                    .SetSource("https://api.nuget.org/v3/index.json")
                    .SetApiKey(NuGetApiKey)
                    .EnableSkipDuplicate());
            }
        });
}
