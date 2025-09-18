using Nuke.Common;
using Nuke.Common.ChangeLog;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

sealed partial class Build
{
    /// <summary>
    ///     Publish a new GitHub release.
    /// </summary>
    Target PublishGitHub => _ => _
        .DependsOn(CreateInstaller, CreateBundle)
        .Requires(() => ReleaseVersion)
        .OnlyWhenStatic(() => IsServerBuild)
        .Executes(async () =>
        {
            var gitHubName = GitRepository.GetGitHubName();
            var gitHubOwner = GitRepository.GetGitHubOwner();

            var artifacts = Directory.GetFiles(ArtifactsDirectory, "*");
            Assert.NotEmpty(artifacts, "No artifacts were found to create the Release");

            var changelogFile = RootDirectory / "CHANGELOG.md";
            var changelog = ChangelogTasks.ReadChangelog(changelogFile);
            var releaseNotes = changelog.ReleaseNotes;

            Log.Information(changelog.Path);

            var newRelease = new NewRelease(ReleaseVersion)
            {
                Name = ReleaseVersion,
                Body = changelog.ToString(),
                TargetCommitish = GitRepository.Commit,
                Prerelease = IsPrerelease
            };

            var release = await GitHubTasks.GitHubClient.Repository.Release.Create(gitHubOwner, gitHubName, newRelease);
            await UploadArtifactsAsync(release, artifacts);
        });

    /// <summary>
    ///     Uploads the artifacts to the GitHub release.
    /// </summary>
    static async Task UploadArtifactsAsync(Release createdRelease, IEnumerable<string> artifacts)
    {
        foreach (var file in artifacts)
        {
            var releaseAssetUpload = new ReleaseAssetUpload
            {
                ContentType = "application/x-binary",
                FileName = Path.GetFileName(file),
                RawData = File.OpenRead(file)
            };

            await GitHubTasks.GitHubClient.Repository.Release.UploadAsset(createdRelease, releaseAssetUpload);
            Log.Information("Artifact: {Path}", file);
        }
    }
}