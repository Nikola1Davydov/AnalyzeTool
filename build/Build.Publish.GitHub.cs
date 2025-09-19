using Nuke.Common;
using Nuke.Common.ChangeLog;
using Nuke.Common.IO;
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
            // MSI-Datei zum ArtifactsDirectory kopieren
            AbsolutePath msiFile = RootDirectory / "Installer" / "Analyse Tool-1.1.0.0-SingleUser.msi";
            if (File.Exists(msiFile))
            {
                Log.Information(msiFile);
                File.Copy(msiFile, Path.Combine(ArtifactsDirectory, "Analyse Tool-1.1.0.0-MultiUser.msi"), true);
                File.Copy(msiFile, Path.Combine(ArtifactsDirectory, "Analyse Tool-1.1.0.0-SingleUser.msi"), true);
            }

            string gitHubName = GitRepository.GetGitHubName();
            string gitHubOwner = GitRepository.GetGitHubOwner();

            string[] artifacts = Directory.GetFiles(ArtifactsDirectory, "*");
            Assert.NotEmpty(artifacts, "No artifacts were found to create the Release");

            AbsolutePath changelogFile = RootDirectory / "CHANGELOG.md";
            ChangeLog changelog = ChangelogTasks.ReadChangelog(changelogFile);
            IReadOnlyList<ReleaseNotes> releaseNotes = changelog.ReleaseNotes;

            Log.Information(changelog.Path);

            NewRelease newRelease = new NewRelease(ReleaseVersion)
            {
                Name = ReleaseVersion,
                Body = changelog.ToString(),
                TargetCommitish = GitRepository.Commit,
                Prerelease = IsPrerelease
            };

            Release release = await GitHubTasks.GitHubClient.Repository.Release.Create(gitHubOwner, gitHubName, newRelease);
            await UploadArtifactsAsync(release, artifacts);
        });

    /// <summary>
    ///     Uploads the artifacts to the GitHub release.
    /// </summary>
    static async Task UploadArtifactsAsync(Release createdRelease, IEnumerable<string> artifacts)
    {
        foreach (string file in artifacts)
        {
            ReleaseAssetUpload releaseAssetUpload = new ReleaseAssetUpload
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