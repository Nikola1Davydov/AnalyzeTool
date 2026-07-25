using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Serilog;
using System.Linq;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     Feed the SDK package is pushed to. Defaults to nuget.org — where AnalyseTool.Sdk has lived
    ///     since 1.0.0 and where `dotnet add package` finds it without any credentials. (An earlier
    ///     draft defaulted to GitHub Packages as a trial feed; that stage never happened, and GitHub
    ///     Packages demands a PAT even to READ public packages, so it serves no audience here.)
    /// </summary>
    [Parameter("NuGet feed to push AnalyseTool.Sdk to. Defaults to nuget.org.")]
    readonly string NuGetSource;

    /// <summary>
    ///     API key for <see cref="NuGetSource"/> — for the default feed, a nuget.org key with Push
    ///     scope for AnalyseTool.Sdk. In CI this arrives as the NUGET_API_KEY repository secret via
    ///     the NuGetApiKey env var (see "Publish Release.yml").
    /// </summary>
    [Parameter("API key for the NuGet feed."), Secret]
    readonly string NuGetApiKey;

    string ResolvedNuGetSource => NuGetSource ?? "https://api.nuget.org/v3/index.json";

    /// <summary>
    ///     Publishes the SDK package — the artifact that lets an extension author depend on a PINNED,
    ///     immutable version instead of checking this repository out by branch. A branch is mutable
    ///     shared state: a bad commit reaches every consumer retroactively, which is exactly how a
    ///     broken MSBuild expression here once failed builds in an unrelated repository.
    ///
    ///     DependsOn(Ci) is the point of this target, not a convenience: TestSdkPackage builds an
    ///     external-author project against the freshly packed nupkg, so a package that cannot be
    ///     consumed never reaches the feed. Published versions cannot be recalled — the gate has to
    ///     be in front.
    /// </summary>
    Target PublishSdkPackage => _ => _
        .DependsOn(Ci)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            AbsolutePath[] packages = SdkNupkgDirectory.GlobFiles("*.nupkg").ToArray();
            Assert.NotEmpty(packages, $"No package to publish in {SdkNupkgDirectory}");

            foreach (AbsolutePath package in packages)
            {
                Log.Information("Pushing {Package} to {Source}", package.Name, ResolvedNuGetSource);
                DotNetNuGetPush(settings => settings
                    .SetTargetPath(package)
                    .SetSource(ResolvedNuGetSource)
                    .SetApiKey(NuGetApiKey)
                    // A version already on the feed is immutable, so re-running the pipeline must be
                    // a no-op rather than a failure.
                    .EnableSkipDuplicate());
            }
        });
}
