using Nuke.Common;
using Nuke.Common.Tools.Git;
using Nuke.Common.Utilities.Collections;
using Serilog;

sealed partial class Build
{
    /// <summary>
    ///     Revert the repository to the previous state after failed release.
    /// </summary>
    /// <remarks>
    ///     Only while nothing has been published yet. Once the GitHub release object exists, deleting
    ///     its tag is worse than the failure it is cleaning up after: the release stays visible and
    ///     points at a tag that no longer exists. A failure during asset upload is therefore left for
    ///     a human to finish or delete deliberately.
    /// </remarks>
    Target CleanFailedRelease => _ => _
        .Unlisted()
        .AssuredAfterFailure()
        .TriggeredBy(PublishGitHub)
        .OnlyWhenStatic(() => IsServerBuild)
        .OnlyWhenDynamic(() => !FailedTargets.IsEmpty())
        .Executes(() =>
        {
            if (_releaseCreated)
            {
                Log.Warning("The GitHub release for {Version} was already created — keeping the tag. " +
                            "Finish or delete the release manually.", ReleaseVersion);
                return;
            }

            Log.Information("Cleaning failed GitHub release");
            GitTasks.Git($"push --delete origin {ReleaseVersion}", logInvocation: false);
        });
}