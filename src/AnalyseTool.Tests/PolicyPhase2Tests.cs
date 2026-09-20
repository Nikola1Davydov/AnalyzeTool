using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Policy;

namespace AnalyseTool.Tests;

/// <summary>
/// Phase 2 of the enterprise design: the company catalog merges between the shipped and the user
/// list, required extensions cannot be switched off, a package pin is honored, and a feed may not
/// send the download to a host the organization did not approve.
/// </summary>
public class PolicyPhase2Tests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-policy2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ExtensionCatalogEntry Entry(string id, string name, string? source = null) =>
        new() { Id = id, Name = name, Source = source };

    [Test]
    public async Task Catalog_merges_shipped_then_policy_then_user_and_the_user_cannot_override_the_organization()
    {
        var shipped = new[] { Entry("a.one", "One", "github:a/one"), Entry("a.two", "Two"), Entry("a.three", "Three") };
        var policy = new[] { Entry("a.two", "Two (company build)", "https://git.company.local/two/feed.json"), Entry("c.std", "Standards") };
        var user = new[] { Entry("a.one", "One (my fork)", "github:me/one"), Entry("c.std", "Standards (mine)"), Entry("u.x", "Mine") };

        IReadOnlyList<ExtensionCatalogEntry> merged = ExtensionSourceCatalog.Merge(shipped, policy, user);

        Dictionary<string, ExtensionCatalogEntry> byId = merged.ToDictionary(e => e.Id);
        await Assert.That(byId.Count).IsEqualTo(5);
        await Assert.That(byId["a.three"].Origin).IsEqualTo("shipped");       // untouched shipped entry keeps its origin
        await Assert.That(byId["a.one"].Origin).IsEqualTo("user");            // user may replace a shipped entry
        await Assert.That(byId["a.one"].Name).IsEqualTo("One (my fork)");
        await Assert.That(byId["a.two"].Origin).IsEqualTo("policy");          // policy replaces shipped
        await Assert.That(byId["c.std"].Origin).IsEqualTo("policy");          // …and the user cannot take it back
        await Assert.That(byId["c.std"].Name).IsEqualTo("Standards");
        await Assert.That(byId["u.x"].UserSupplied).IsTrue();
        // shipped first, then organization, then the user's own additions
        await Assert.That(merged.Select(e => e.Origin).Distinct().ToArray()).IsEquivalentTo(new[] { "shipped", "policy", "user" });
    }

    [Test]
    [Arguments("github:acme/tool", "https://github.com/acme/tool/releases/download/v1/tool-1.zip", true)]
    [Arguments("github:acme/tool", "https://objects.githubusercontent.com/abc", true)]
    [Arguments("github:acme/tool", "https://evil.example/tool.zip", false)]
    [Arguments("https://git.company.local/bim/feed.json", "https://git.company.local/bim/tool-1.zip", true)]   // same host
    [Arguments("https://git.company.local/bim/feed.json", "https://cdn.company.local/tool-1.zip", true)]        // whitelisted host
    [Arguments("https://git.company.local/bim/feed.json", "https://evil.example/tool-1.zip", false)]
    [Arguments("https://git.company.local/bim/feed.json", "http://git.company.local/bim/tool-1.zip", false)]    // plain http never
    public async Task A_feed_may_only_send_the_download_to_its_own_host_or_an_approved_one(string feed, string download, bool expected)
    {
        string[] allowed = ["github:acme/", "https://git.company.local/", "https://cdn.company.local/"];
        await Assert.That(PolicyFeedRules.IsDownloadAllowed(ExtensionUpdateFeed.Normalize(feed), download, allowed)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_sha256_pin_refuses_a_package_that_does_not_match_and_accepts_one_that_does()
    {
        string file = Path.Combine(_dir, "pkg.zip");
        File.WriteAllBytes(file, [1, 2, 3, 4, 5]);
        string good = PackageHash.Sha256Of(file);

        await Assert.That(PackageHash.Verify(file, null, "x")).IsNull();                       // no pin = nothing to check
        await Assert.That(PackageHash.Verify(file, good, "x")).IsNull();
        await Assert.That(PackageHash.Verify(file, "sha256:" + good.ToLowerInvariant(), "x")).IsNull();
        await Assert.That(PackageHash.Verify(file, new string('0', 64), "x")).IsNotNull();
        await Assert.That(PackageHash.Verify(file, "not-a-hash", "x")).IsNull();               // an unusable pin pins nothing
    }

    [Test]
    public async Task Source_reader_refuses_plain_http_and_reads_a_path_with_env_expansion()
    {
        await Assert.That(PolicySourceReader.Validate("http://x/catalog.json")).IsNotNull();
        await Assert.That(PolicySourceReader.Validate("https://x/catalog.json")).IsNull();
        await Assert.That(PolicySourceReader.Validate("source:bimtools/catalog.json")).IsNull();   // a named source is a valid form
        await Assert.That(PolicySourceReader.Validate("source:")).IsNotNull();                     // …but it must name one
        await Assert.That(PolicySourceReader.Validate(@"\\server\share\catalog.json")).IsNull();

        string file = Path.Combine(_dir, "catalog.json");
        File.WriteAllText(file, "{\"entries\":[]}");
        Environment.SetEnvironmentVariable("AT_TEST_DIR", _dir);
        try
        {
            PolicySourceContent? read = PolicySourceReader.ReadCached("%AT_TEST_DIR%" + Path.DirectorySeparatorChar + "catalog.json");
            await Assert.That(read).IsNotNull();
            await Assert.That(read!.Text).IsEqualTo("{\"entries\":[]}");
            await Assert.That(read.FromCache).IsFalse();

            (PolicySourceContent? async, string? problem) = await PolicySourceReader.ReadAsync(file, CancellationToken.None);
            await Assert.That(problem).IsNull();
            await Assert.That(async!.Text).IsEqualTo("{\"entries\":[]}");

            (PolicySourceContent? missing, string? why) = await PolicySourceReader.ReadAsync(Path.Combine(_dir, "nope.json"), CancellationToken.None);
            await Assert.That(missing).IsNull();
            await Assert.That(why).IsNotNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AT_TEST_DIR", null);
        }
    }

    [Test]
    [NotInParallel("PolicyStore")]
    public async Task A_required_extension_is_always_enabled_and_cannot_be_disabled()
    {
        string path = Path.Combine(_dir, "policy.json");
        File.WriteAllText(path, """
            { "version": 1,
              "organization": { "name": "Contoso BIM" },
              "extensions": { "required": [ { "id": "company.standards", "source": "github:contoso/standards", "sha256": null } ],
                              "catalogUrl": "http://insecure/catalog.json" } }
            """);
        try
        {
            PolicyStore.OverridePathForTests(path);

            await Assert.That(RequiredExtensions.IsRequired("Company.Standards")).IsTrue();
            await Assert.That(RequiredExtensions.IsRequired("other")).IsFalse();

            // even a stored "disabled" flag is overruled — and nothing is written to flip it
            await Assert.That(ExtensionStateStore.IsEnabled("company.standards")).IsTrue();
            await Assert.That(() => { ExtensionStateStore.SetEnabled("company.standards", enabled: false); }).Throws<InvalidOperationException>();
            await Assert.That(RequiredExtensions.RefusalFor("company.standards", "disabled")).Contains("Contoso BIM");

            // a plain-http catalog source is dropped, not fetched
            await Assert.That(ExtensionSourceCatalog.PolicyCatalogSource).IsNull();
        }
        finally
        {
            PolicyStore.OverridePathForTests(null);
        }
    }
}
