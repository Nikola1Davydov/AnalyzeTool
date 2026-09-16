using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Policy;

namespace AnalyseTool.Tests;

/// <summary>
/// The organization policy as the host reads it (docs/enterprise-deployment-design.md, phase 1):
/// a missing or broken file changes nothing, a present one is applied with its problems named, a
/// locked value wins and an unlocked one is only a default.
/// </summary>
public class PolicyTests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string json)
    {
        string path = Path.Combine(_dir, "policy.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Test]
    public async Task A_missing_file_is_absent_and_locks_nothing()
    {
        PolicyState state = PolicyStore.Load(Path.Combine(_dir, "policy.json"));

        await Assert.That(state.Origin).IsEqualTo(PolicyOrigin.Absent);
        await Assert.That(state.IsPresent).IsFalse();
        await Assert.That(state.Problems).IsEmpty();
        await Assert.That(state.IsLocked(PolicySettings.CodeExecutionEnabled)).IsFalse();
    }

    [Test]
    public async Task A_broken_file_is_reported_and_ignored_not_fatal()
    {
        PolicyState state = PolicyStore.Load(Write("{ this is not json"));

        await Assert.That(state.Origin).IsEqualTo(PolicyOrigin.Invalid);
        await Assert.That(state.IsPresent).IsFalse();
        await Assert.That(state.Problems.Count).IsEqualTo(1);
        await Assert.That(state.Problems[0]).Contains("Could not read");
        // The document is empty, so every accessor answers "nothing configured".
        await Assert.That(state.Document.CodeExecution).IsNull();
        await Assert.That(state.Document.Locked).IsEmpty();
    }

    [Test]
    public async Task Unknown_keys_and_unknown_locks_are_warnings_beside_a_working_policy()
    {
        PolicyState state = PolicyStore.Load(Write("""
            { "version": 1,
              "codeExecution": { "enabled": false },
              "locked": ["codeExecution.enabled", "telemetry.sink"],
              "futureSection": { "x": 1 } }
            """));

        await Assert.That(state.Origin).IsEqualTo(PolicyOrigin.Loaded);
        await Assert.That(state.IsLocked(PolicySettings.CodeExecutionEnabled)).IsTrue();
        await Assert.That(state.Problems.Any(p => p.Contains("futureSection"))).IsTrue();
        await Assert.That(state.Problems.Any(p => p.Contains("telemetry.sink"))).IsTrue();
    }

    [Test]
    public async Task A_pointer_file_is_recognized_and_named_as_not_yet_followed()
    {
        PolicyState state = PolicyStore.Load(Write("""{ "version": 1, "policyUrl": "https://x/policy.json", "enforced": true }"""));

        await Assert.That(state.Document.IsPointer).IsTrue();
        await Assert.That(state.Problems.Any(p => p.Contains("policyUrl"))).IsTrue();
    }

    [Test]
    public async Task Lock_names_are_case_insensitive()
    {
        PolicyState state = PolicyStore.Load(Write("""{ "version": 1, "mcp": { "enabled": false }, "locked": ["MCP.Enabled"] }"""));

        await Assert.That(state.IsLocked(PolicySettings.McpEnabled)).IsTrue();
    }

    [Test]
    public async Task A_locked_value_wins_over_the_user_and_an_unlocked_one_is_only_a_default()
    {
        PolicyState locked = PolicyStore.Load(Write("""{ "version": 1, "codeExecution": { "enabled": false }, "locked": ["codeExecution.enabled"] }"""));
        PolicyState unlocked = PolicyStore.Load(Write("""{ "version": 1, "codeExecution": { "enabled": true } }"""));
        PolicyState absent = PolicyState.Absent("nowhere");
        const string s = PolicySettings.CodeExecutionEnabled;

        // locked: policy beats the user's explicit true
        await Assert.That(locked.Resolve(s, locked.Document.CodeExecution!.Enabled, userValue: true, fallback: false)).IsFalse();
        await Assert.That(locked.OriginOf(s, locked.Document.CodeExecution!.Enabled, userValue: true)).IsEqualTo("policy");

        // unlocked: the user's own choice wins, the policy fills in only when there is none
        await Assert.That(unlocked.Resolve(s, unlocked.Document.CodeExecution!.Enabled, userValue: false, fallback: false)).IsFalse();
        await Assert.That(unlocked.Resolve(s, unlocked.Document.CodeExecution!.Enabled, userValue: null, fallback: false)).IsTrue();
        await Assert.That(unlocked.OriginOf(s, unlocked.Document.CodeExecution!.Enabled, userValue: null)).IsEqualTo("policy-default");
        await Assert.That(unlocked.OriginOf(s, unlocked.Document.CodeExecution!.Enabled, userValue: false)).IsEqualTo("user");

        // no policy: user, then the built-in default
        await Assert.That(absent.Resolve<bool>(s, null, userValue: true, fallback: false)).IsTrue();
        await Assert.That(absent.Resolve<bool>(s, null, userValue: null, fallback: false)).IsFalse();
        await Assert.That(absent.OriginOf<bool>(s, null, null)).IsEqualTo("default");
    }

    [Test]
    public async Task The_locked_message_names_the_organization_and_the_file()
    {
        PolicyState state = PolicyStore.Load(Write("""{ "version": 1, "organization": { "name": "Contoso BIM" }, "mcp": { "enabled": false }, "locked": ["mcp.enabled"] }"""));

        string message = state.LockedMessage(PolicySettings.McpEnabled);
        await Assert.That(message).Contains("Contoso BIM");
        await Assert.That(message).Contains("mcp.enabled");
        await Assert.That(message).Contains(state.Path);
    }

    [Test]
    [Arguments("github:Nikola1Davydov/AnalyseTool.FamilyManager", true)]
    [Arguments("https://github.com/Nikola1Davydov/AnalyseTool.FamilyManager", true)]  // pasted URL, normalized first
    [Arguments("Nikola1Davydov/AnalyseTool.FamilyManager", true)]                     // bare owner/repo
    [Arguments("github:Nikola1DavydovEvil/Something", false)]                         // owner prefix must end at the slash
    [Arguments("github:someone-else/repo", false)]
    [Arguments("https://git.company.local/bim/feed.json", true)]
    [Arguments("https://git.company.local.evil.com/feed.json", false)]
    [Arguments("https://other.host/feed.json", false)]
    public async Task Allowed_feeds_are_prefixes_in_the_normalized_form(string source, bool expected)
    {
        string[] allowed = ["github:Nikola1Davydov", "https://git.company.local/"];

        bool actual = PolicyFeedRules.IsAllowed(ExtensionUpdateFeed.Normalize(source), allowed);
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments(@"C:\Users\me\Contoso\BIM Tools - Documents\AnalyseTool", true)]
    [Arguments(@"C:\Users\me\Contoso\BIM Tools - Documents", true)]
    [Arguments(@"\\fileserver\revit\analysetool\extensions", false)]
    [Arguments(@"C:\Tools\AnalyseTool\extensions", false)]
    public async Task A_synced_library_path_is_recognized_so_it_can_be_warned_about(string path, bool expected)
    {
        // Independent of the OneDrive env vars (not set on CI): the " - Documents" library marker alone decides.
        await Assert.That(ExtensionSources.LooksSynced(path)).IsEqualTo(expected);
    }

    [Test]
    public async Task An_empty_whitelist_allows_nothing_and_a_blank_entry_is_skipped()
    {
        await Assert.That(PolicyFeedRules.IsAllowed("github:a/b", Array.Empty<string>())).IsFalse();
        await Assert.That(PolicyFeedRules.IsAllowed("github:a/b", ["", "  "])).IsFalse();
    }

    // The store's global state is read by every settings class, so the tests that go through it
    // run one at a time and restore the default path afterwards.
    [Test]
    [NotInParallel("PolicyStore")]
    public async Task The_store_applies_the_file_it_is_pointed_at_and_reloads()
    {
        string path = Write("""
            { "version": 1,
              "extensions": { "roots": ["%TEMP%\\at-policy-root", "\\\\server\\share\\ext"],
                              "allowedFeeds": ["github:acme/"],
                              "allowInstallFromRepository": false },
              "mcp": { "enabled": true },
              "locked": ["extensions.roots"] }
            """);
        try
        {
            PolicyStore.OverridePathForTests(path);

            await Assert.That(PolicyStore.Current.IsPresent).IsTrue();

            // %TEMP% expanded, both roots present, marked as coming from the policy
            IReadOnlyList<string> roots = ExtensionSources.PolicyRoots();
            await Assert.That(roots.Count).IsEqualTo(2);
            await Assert.That(roots[0]).DoesNotContain("%TEMP%");
            await Assert.That(ExtensionSources.AllRoots().Count(r => r.FromPolicy)).IsEqualTo(2);
            await Assert.That(ExtensionSources.RootsLocked).IsTrue();

            // the machine root is listed only when an administrator created it, and then first
            IReadOnlyList<ExtensionSourceRoot> all = ExtensionSources.AllRoots();
            bool machineExists = Directory.Exists(ExtensionSources.MachineManagedRoot);
            await Assert.That(all.Any(r => r.Zone == ExtensionZone.Machine)).IsEqualTo(machineExists);
            if (machineExists)
                await Assert.That(all[0].Zone).IsEqualTo(ExtensionZone.Machine);

            // feed rules read the same state
            await Assert.That(PolicyFeedRules.Refusal("github:acme/tool")).IsNull();
            await Assert.That(PolicyFeedRules.Refusal("github:other/tool")).IsNotNull();
            await Assert.That(PolicyFeedRules.InstallFromRepositoryAllowed).IsFalse();

            // a locked root list refuses add and remove with the policy's own message
            await Assert.That(() => { ExtensionSources.AddRoot(Path.Combine(_dir, "mine")); }).Throws<InvalidOperationException>();
            await Assert.That(() => { ExtensionSources.RemoveRoot(roots[0]); }).Throws<InvalidOperationException>();

            // the file gone → reload → single-seat behavior again
            File.Delete(path);
            PolicyStore.Reload();
            await Assert.That(PolicyStore.Current.IsPresent).IsFalse();
            await Assert.That(ExtensionSources.PolicyRoots()).IsEmpty();
            await Assert.That(PolicyFeedRules.Refusal("github:other/tool")).IsNull();
            await Assert.That(PolicyFeedRules.InstallFromRepositoryAllowed).IsTrue();
        }
        finally
        {
            PolicyStore.OverridePathForTests(null);
        }
    }
}
