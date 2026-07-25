using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitHub;
using Serilog;
using System.Linq;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    /// <summary>
    ///     Feed the SDK package is pushed to. Defaults to this repository's GitHub Packages feed —
    ///     the private feed the platform is trialled on before the contract goes to nuget.org.
    /// </summary>
    [Parameter("NuGet feed to push AnalyseTool.Sdk to. Defaults to this repo's GitHub Packages feed.")]
    readonly string NuGetSource;

    /// <summary>
    ///     API key for <see cref="NuGetSource"/>. For GitHub Packages a token with <c>write:packages</c>.
    /// </summary>
    [Parameter("API key for the NuGet feed."), Secret]
    readonly string NuGetApiKey;

    string ResolvedNuGetSource =>
        NuGetSource ?? $"https://nuget.pkg.github.com/{GitRepository.GetGitHubOwner()}/index.json";

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
