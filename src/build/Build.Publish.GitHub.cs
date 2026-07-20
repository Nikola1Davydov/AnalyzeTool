using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // Alle MSI-Dateien ins ArtifactsDirectory kopieren. Solution-relative, so it works wherever
            // the solution sits in the repo (e.g. moved under src/).
            var installerOutput = Solution.Delivery.Installer.Directory / "output";

            foreach (var msi in installerOutput.GlobFiles("*.msi"))
            {
                var fileName = Path.GetFileName(msi);
                var target = ArtifactsDirectory / fileName;

                Log.Information("Copying {File}", msi);
                File.Copy(msi, target, overwrite: true);
            }

            // GitHub Daten
            string gitHubName = GitRepository.GetGitHubName();
            string gitHubOwner = GitRepository.GetGitHubOwner();

            string[] artifacts = Directory.GetFiles(ArtifactsDirectory, "*");
            Assert.NotEmpty(artifacts, "No artifacts were found to create the Release");

            AbsolutePath changelogFile = RootDirectory / "CHANGELOG.md";
            string releaseNotesBody = ReadReleaseNotesBody(changelogFile, ReleaseVersion);
            Log.Information("Using release notes from {Path}", changelogFile);

            NewRelease newRelease = new NewRelease(ReleaseVersion)
            {
                Name = ReleaseVersion,
                Body = releaseNotesBody,
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

    static string ReadReleaseNotesBody(string changelogFile, string releaseVersion)
    {
        Assert.True(!string.IsNullOrWhiteSpace(releaseVersion), "Release version must be provided");

        string[] lines = File.ReadAllLines(changelogFile);
        string[] acceptedHeaders =
        [
            $"## [{releaseVersion}]",
            $"## [v{releaseVersion}]"
        ];

        int start = Array.FindIndex(lines, line => acceptedHeaders.Any(header => line.StartsWith(header, StringComparison.OrdinalIgnoreCase)));
        Assert.True(start >= 0, $"Version section for '{releaseVersion}' was not found in {changelogFile}");

        int end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## [", StringComparison.Ordinal));
        if (end < 0)
            end = lines.Length;

        var notes = lines
            .Skip(start + 1)
            .Take(end - start - 1)
            .Where(line => line.TrimStart().StartsWith("- "))
            .Select(line => line.Trim())
            .ToArray();

        Assert.NotEmpty(notes, $"Release Notes should not be empty for version '{releaseVersion}'");
        return string.Join(Environment.NewLine, notes);
    }
}