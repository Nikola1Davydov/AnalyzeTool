using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Features.Extensions;

namespace AnalyseTool.Tests;

/// <summary>Update feeds without the network: which pasted forms count as GitHub, how a release's
/// package is picked, what a JSON feed must carry, and what a served package must be before it may
/// replace the installed one.</summary>
public class ExtensionUpdateFeedTests
{
    [Test]
    [Arguments("https://github.com/acme/walls", "github:acme/walls")]
    [Arguments("https://github.com/acme/walls.git", "github:acme/walls")]
    [Arguments("https://api.github.com/repos/acme/walls/releases/latest", "github:acme/walls")]
    [Arguments(" acme/walls ", "github:acme/walls")]
    [Arguments("https://acme.example/feed.json", "https://acme.example/feed.json")]
    public async Task Pasted_forms_fold_onto_github_owner_repo(string pasted, string expected)
    {
        await Assert.That(ExtensionUpdateFeed.Normalize(pasted)).IsEqualTo(expected);
    }

    private static string Release(string tag, params string[] zipNames) =>
        new Newtonsoft.Json.Linq.JObject
        {
            ["tag_name"] = tag,
            ["html_url"] = "https://github.com/acme/walls/releases/" + tag,
            ["assets"] = new Newtonsoft.Json.Linq.JArray(zipNames.Select(n => new Newtonsoft.Json.Linq.JObject
            {
                ["name"] = n,
                ["browser_download_url"] = "https://github.com/acme/walls/releases/download/" + n,
            })),
        }.ToString();

    [Test]
    public async Task A_release_with_one_zip_is_that_zip_and_the_tag_is_the_version()
    {
        ExtensionUpdateInfo info = ExtensionUpdateFeed.FromGithubRelease(Release("v1.2.0", "anything.zip"), "acme", "walls", "acme.walls");

        await Assert.That(info.Version).IsEqualTo("1.2.0");
        await Assert.That(info.DownloadUrl).EndsWith("anything.zip");
    }

    [Test]
    public async Task Among_several_zips_the_one_named_id_version_is_picked()
    {
        ExtensionUpdateInfo info = ExtensionUpdateFeed.FromGithubRelease(
            Release("1.2.0", "acme.walls-1.1.0.zip", "acme.walls-1.2.0.zip"), "acme", "walls", "acme.walls");

        await Assert.That(info.DownloadUrl).EndsWith("acme.walls-1.2.0.zip");
    }

    [Test]
    public async Task An_ambiguous_or_empty_release_is_refused()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => ExtensionUpdateFeed.FromGithubRelease(Release("1.2.0", "a.zip", "b.zip"), "acme", "walls", "acme.walls"))
                .Throws<InvalidOperationException>().WithMessageContaining("cannot be identified");
            await Assert.That(() => ExtensionUpdateFeed.FromGithubRelease(Release("1.2.0", "notes.txt"), "acme", "walls", "acme.walls"))
                .Throws<InvalidOperationException>().WithMessageContaining("no .zip");
            await Assert.That(() => ExtensionUpdateFeed.FromGithubRelease("{}", "acme", "walls", "acme.walls"))
                .Throws<InvalidOperationException>().WithMessageContaining("no published release");
        }
    }

    [Test]
    public async Task A_json_feed_needs_a_version_and_a_download_url()
    {
        ExtensionUpdateInfo info = ExtensionUpdateFeed.FromJsonFeed("""{ "version": "2.0.0", "downloadUrl": "https://acme.example/w.zip" }""");
        await Assert.That(info.Version).IsEqualTo("2.0.0");

        await Assert.That(() => ExtensionUpdateFeed.FromJsonFeed("""{ "version": "2.0.0" }""")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Unsupported_feeds_and_plain_http_downloads_are_refused_before_any_request()
    {
        using (Assert.Multiple())
        {
            await Assert.That(async () => await ExtensionUpdateFeed.ResolveAsync("ftp://acme.example/feed", "acme.walls", CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(async () => await ExtensionUpdateFeed.DownloadPackageAsync("http://acme.example/w.zip", "acme.walls", CancellationToken.None))
                .Throws<InvalidOperationException>();
        }
    }

    [Test]
    [Arguments("1.2.0", "1.1.9", true)]
    [Arguments("v1.10.0", "1.9.0", true)]
    [Arguments("1.2.0", "1.2.0", false)]
    [Arguments("1.2.0-beta", "1.1.0", true)]
    [Arguments("nightly", "1.0.0", true)]
    public async Task Newer_is_compared_as_versions(string latest, string installed, bool expected)
    {
        await Assert.That(CheckExtensionUpdates.IsNewer(latest, installed)).IsEqualTo(expected);
    }

    private static ExtensionPackageInfo Served(string id, string version) =>
        new(new ExtensionManifest { Id = id, Version = version }, "", Array.Empty<string>());

    [Test]
    public async Task A_served_package_must_be_the_same_extension_and_newer()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => UpdateExtension.CheckServedPackage("acme.walls", "1.2.0", Served("evil.other", "9.0.0"), "1.0.0"))
                .Throws<InvalidOperationException>().WithMessageContaining("evil.other");
            await Assert.That(() => UpdateExtension.CheckServedPackage("acme.walls", "1.2.0", Served("acme.walls", "1.0.0"), "1.0.0"))
                .Throws<InvalidOperationException>().WithMessageContaining("downgrade");
        }
        UpdateExtension.CheckServedPackage("acme.walls", "1.2.0", Served("acme.walls", "1.2.0"), "1.0.0"); // passes
    }
}
