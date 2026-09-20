using AnalyseTool.Core.Common.Policy;

namespace AnalyseTool.Tests;

/// <summary>
/// Phase 3 of the enterprise design: named sources resolve per seat, two policy layers merge with
/// the machine layer on top, a detached signature is honored, and discovery turns what a user typed
/// into locations to try.
/// </summary>
public class PolicyPhase3Tests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-policy3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- 3c: signatures --------------------------------------------------------------------------

    [Test]
    public async Task A_signed_policy_verifies_a_tampered_one_does_not_and_no_key_means_no_check()
    {
        (string pub, string priv) = PolicySignature.GenerateKeyPair();
        byte[] policy = PolicySignature.Utf8("""{ "version": 1, "organization": { "name": "Contoso" } }""");
        string sig = PolicySignature.Sign(priv, policy);

        await Assert.That(PolicySignature.Verify(pub, policy, sig)).IsEqualTo(SignatureState.Verified);
        await Assert.That(PolicySignature.Verify(pub, PolicySignature.Utf8("{ \"version\": 1 }"), sig)).IsEqualTo(SignatureState.Invalid);
        await Assert.That(PolicySignature.Verify(pub, policy, null)).IsEqualTo(SignatureState.Missing);
        await Assert.That(PolicySignature.Verify(null, policy, sig)).IsEqualTo(SignatureState.NoKey);
        await Assert.That(PolicySignature.Verify(pub, policy, "not base64!")).IsEqualTo(SignatureState.Invalid);

        (string otherPub, _) = PolicySignature.GenerateKeyPair();
        await Assert.That(PolicySignature.Verify(otherPub, policy, sig)).IsEqualTo(SignatureState.Invalid);

        string? fp = PolicySignature.Fingerprint(pub);
        await Assert.That(fp).IsNotNull();
        await Assert.That(fp!.Length).IsEqualTo(19); // 4 groups of 4 hex + 3 spaces
    }

    [Test]
    public async Task Fetching_a_signed_policy_from_a_folder_verifies_it_and_refuses_it_without_a_signature()
    {
        (string pub, string priv) = PolicySignature.GenerateKeyPair();
        string text = """{ "version": 1, "organization": { "name": "Contoso BIM", "contact": "bim@contoso.com" }, "mcp": { "enabled": false } }""";
        string file = Path.Combine(_dir, "policy.json");
        File.WriteAllText(file, text);

        OrgPolicyFetch unsigned = await OrgPolicySource.FetchAsync(file, pub, null, CancellationToken.None);
        await Assert.That(unsigned.Accepted).IsFalse();
        await Assert.That(unsigned.State).IsEqualTo(SignatureState.Missing);

        File.WriteAllText(file + ".sig", PolicySignature.Sign(priv, File.ReadAllBytes(file)));
        OrgPolicyFetch signed = await OrgPolicySource.FetchAsync(file, pub, null, CancellationToken.None);
        await Assert.That(signed.Accepted).IsTrue();
        await Assert.That(signed.State).IsEqualTo(SignatureState.Verified);
        await Assert.That(signed.Document!.Organization!.Name).IsEqualTo("Contoso BIM");

        // no key known: accepted as-is (HTTPS + preview are the defense in v1)
        OrgPolicyFetch nokey = await OrgPolicySource.FetchAsync(file, null, null, CancellationToken.None);
        await Assert.That(nokey.Accepted).IsTrue();
        await Assert.That(nokey.State).IsEqualTo(SignatureState.NoKey);
    }

    [Test]
    public async Task A_joinable_policy_must_name_its_organization_and_may_not_be_a_pointer()
    {
        string anonymous = Path.Combine(_dir, "a.json");
        File.WriteAllText(anonymous, """{ "version": 1, "mcp": { "enabled": false } }""");
        OrgPolicyFetch a = await OrgPolicySource.FetchAsync(anonymous, null, null, CancellationToken.None);
        await Assert.That(a.Accepted).IsFalse();
        await Assert.That(a.Problem!).Contains("organization.name");

        string pointer = Path.Combine(_dir, "p.json");
        File.WriteAllText(pointer, """{ "version": 1, "policyUrl": "https://x/policy.json" }""");
        OrgPolicyFetch p = await OrgPolicySource.FetchAsync(pointer, null, null, CancellationToken.None);
        await Assert.That(p.Accepted).IsFalse();
        await Assert.That(p.Problem!).Contains("pointer");
    }

    // ---- 3b: two layers --------------------------------------------------------------------------

    [Test]
    public async Task The_machine_layer_supplies_a_section_over_the_organization_and_locks_follow_the_supplying_layer()
    {
        PolicyDocument machine = new()
        {
            Mcp = new McpPolicy { Enabled = false },
            Locked = new List<string> { "mcp.enabled" },
        };
        PolicyDocument org = new()
        {
            Organization = new PolicyOrganization { Name = "Contoso" },
            Mcp = new McpPolicy { Enabled = true },
            CodeExecution = new CodeExecutionPolicy { Enabled = false },
            Locked = new List<string> { "mcp.enabled", "codeExecution.enabled" },
        };

        PolicyDocument merged = PolicyStore.Merge(machine, org);

        await Assert.That(merged.Mcp!.Enabled).IsFalse();                        // machine wins the section
        await Assert.That(merged.CodeExecution!.Enabled).IsFalse();              // org fills what machine lacks
        await Assert.That(merged.Organization!.Name).IsEqualTo("Contoso");
        await Assert.That(merged.Locked).Contains("mcp.enabled");                // locked by machine
        await Assert.That(merged.Locked).Contains("codeExecution.enabled");      // locked by org, which supplies it

        // machine supplies mcp WITHOUT locking it → the org's lock on mcp does not apply
        PolicyDocument machineUnlocked = new() { Mcp = new McpPolicy { Enabled = false } };
        await Assert.That(PolicyStore.Merge(machineUnlocked, org).Locked).DoesNotContain("mcp.enabled");

        await Assert.That(PolicyStore.Merge(machine, null)).IsSameReferenceAs(machine);
    }

    [Test]
    [NotInParallel("PolicyStore")]
    public async Task A_machine_pointer_makes_the_organization_layer_mandatory_and_reads_the_cached_policy()
    {
        string machine = Path.Combine(_dir, "policy.json");
        File.WriteAllText(machine, """{ "version": 1, "policyUrl": "https://git.contoso.com/bim/policy.json", "enforced": true }""");
        OrgMembership cached = new()
        {
            PolicyUrl = "https://git.contoso.com/bim/policy.json",
            CachedPolicy = """{ "version": 1, "organization": { "name": "Contoso BIM", "contact": "x" }, "codeExecution": { "enabled": false }, "locked": ["codeExecution.enabled"] }""",
        };
        try
        {
            PolicyStore.OverridePathForTests(machine);
            PolicyStore.OverrideMembershipForTests(() => cached);

            PolicyState state = PolicyStore.Current;
            await Assert.That(state.IsPresent).IsTrue();
            await Assert.That(state.PointerEnforced).IsTrue();
            await Assert.That(state.Organization).IsNotNull();
            await Assert.That(state.IsLocked(PolicySettings.CodeExecutionEnabled)).IsTrue();
            await Assert.That(state.Document.Organization!.Name).IsEqualTo("Contoso BIM");
            await Assert.That(state.LayerOf("codeExecution")).IsEqualTo("organization");
            await Assert.That(state.Problems.Any(p => p.Contains("policyUrl"))).IsFalse();

            // a cache for ANOTHER url is not this pointer's policy
            PolicyStore.OverrideMembershipForTests(() => new OrgMembership { PolicyUrl = "https://elsewhere/policy.json", CachedPolicy = cached.CachedPolicy });
            await Assert.That(PolicyStore.Current.Organization).IsNull();
            await Assert.That(PolicyStore.Current.Problems.Any(p => p.Contains("not been fetched"))).IsTrue();
        }
        finally
        {
            PolicyStore.OverrideMembershipForTests(null);
            PolicyStore.OverridePathForTests(null);
        }
    }

    [Test]
    [NotInParallel("PolicyStore")]
    public async Task Without_a_machine_file_a_joined_seat_runs_on_the_organization_policy_alone()
    {
        OrgMembership joined = new()
        {
            PolicyUrl = Path.Combine(_dir, "policy.json"),
            CachedPolicy = """{ "version": 1, "organization": { "name": "Contoso BIM", "contact": "x" }, "mcp": { "enabled": true } }""",
        };
        try
        {
            PolicyStore.OverridePathForTests(Path.Combine(_dir, "no-machine-file.json"));
            PolicyStore.OverrideMembershipForTests(() => joined);

            PolicyState state = PolicyStore.Current;
            await Assert.That(state.IsPresent).IsTrue();
            await Assert.That(state.PointerEnforced).IsFalse();
            await Assert.That(state.Document.Mcp!.Enabled).IsTrue();
            await Assert.That(state.IsLocked(PolicySettings.McpEnabled)).IsFalse(); // a default, not a lock
            await Assert.That(state.Resolve(PolicySettings.McpEnabled, state.Document.Mcp!.Enabled, userValue: false, fallback: false)).IsFalse();
        }
        finally
        {
            PolicyStore.OverrideMembershipForTests(null);
            PolicyStore.OverridePathForTests(null);
        }
    }

    // ---- 3a: sources and discovery ---------------------------------------------------------------

    [Test]
    [Arguments("source:bimtools/packages/feed.json", "bimtools", "packages/feed.json")]
    [Arguments("source:bimtools", "bimtools", "")]
    [Arguments(@"source:share\extensions", "share", "extensions")]
    public async Task A_source_reference_splits_into_name_and_relative_path(string reference, string name, string relative)
    {
        (string Name, string Relative)? parsed = PolicySourceResolver.Parse(reference);
        await Assert.That(parsed).IsNotNull();
        await Assert.That(parsed!.Value.Name).IsEqualTo(name);
        await Assert.That(parsed.Value.Relative).IsEqualTo(relative);
        await Assert.That(PolicySourceResolver.Parse("https://x/y")).IsNull();
    }

    [Test]
    [NotInParallel("PolicyStore")]
    public async Task Path_and_url_sources_resolve_and_an_unknown_or_missing_one_is_reported()
    {
        string folder = Path.Combine(_dir, "share");
        Directory.CreateDirectory(folder);
        string machine = Path.Combine(_dir, "policy.json");
        File.WriteAllText(machine, $$"""
            { "version": 1,
              "sources": { "share": { "path": {{Newtonsoft.Json.JsonConvert.ToString(folder)}} },
                           "web":   { "url": "https://git.contoso.com/bim/" },
                           "lib":   { "sharepoint": "https://contoso.sharepoint.com/sites/BIM/Shared Documents/AnalyseTool", "markerId": "contoso.bimtools" } } }
            """);
        try
        {
            PolicyStore.OverridePathForTests(machine);
            PolicyStore.OverrideMembershipForTests(() => null);

            await Assert.That(PolicySourceResolver.Resolve("source:share/extensions", out _)).IsEqualTo(Path.Combine(folder, "extensions"));
            await Assert.That(PolicySourceResolver.Resolve("source:web/catalog.json", out _)).IsEqualTo("https://git.contoso.com/bim/catalog.json");
            await Assert.That(PolicySourceResolver.Resolve("https://plain/url", out _)).IsEqualTo("https://plain/url");

            await Assert.That(PolicySourceResolver.Resolve("source:nope/x", out string? unknown)).IsNull();
            await Assert.That(unknown!).Contains("no source named");

            // not synced here → unresolved, remembered for the panel; a picked folder answers it
            await Assert.That(PolicySourceResolver.Resolve("source:lib/packages", out string? why)).IsNull();
            await Assert.That(why!).Contains("not synced");
            await Assert.That(PolicySourceResolver.UnresolvedSources().ContainsKey("lib")).IsTrue();

            PolicySourceResolver.SetOverride("lib", folder);
            try
            {
                await Assert.That(PolicySourceResolver.Resolve("source:lib/packages", out _)).IsEqualTo(Path.Combine(folder, "packages"));
                await Assert.That(PolicySourceResolver.UnresolvedSources().ContainsKey("lib")).IsFalse();
            }
            finally
            {
                PolicySourceResolver.SetOverride("lib", null);
            }
        }
        finally
        {
            PolicyStore.OverrideMembershipForTests(null);
            PolicyStore.OverridePathForTests(null);
        }
    }

    [Test]
    public async Task A_library_url_maps_to_its_local_folder_plus_the_remaining_segments()
    {
        Uri library = new("https://contoso.sharepoint.com/sites/BIM/Shared Documents");
        string local = Path.Combine(_dir, "Contoso", "BIM - Documents");

        string? inside = OneDriveLibraries.MatchLibrary(new Uri("https://contoso.sharepoint.com/sites/BIM/Shared%20Documents/AnalyseTool/packages"), library, local);
        await Assert.That(inside).IsEqualTo(Path.Combine(local, "AnalyseTool", "packages"));
        await Assert.That(OneDriveLibraries.MatchLibrary(new Uri("https://contoso.sharepoint.com/sites/Other/Shared Documents/x"), library, local)).IsNull();
        await Assert.That(OneDriveLibraries.MatchLibrary(new Uri("https://other.sharepoint.com/sites/BIM/Shared Documents/x"), library, local)).IsNull();

        // the settings line the OneDrive client writes: any quoted https URL with any quoted local path
        var pairs = OneDriveLibraries.ParseMappingLine(
            @"libraryScope = 0 F1 ""ODB"" 1 ""Contoso"" ""https://contoso.sharepoint.com/sites/BIM/Shared Documents"" ""C:\Users\me\Contoso\BIM - Documents"" 0 1").ToList();
        await Assert.That(pairs.Count).IsEqualTo(1);
        await Assert.That(pairs[0].LocalPath).IsEqualTo(@"C:\Users\me\Contoso\BIM - Documents");
    }

    [Test]
    public async Task A_marker_file_locates_a_folder_and_a_wrong_id_does_not()
    {
        string root = Path.Combine(_dir, "Contoso", "BIM Tools - Documents");
        string target = Path.Combine(root, "AnalyseTool");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, PolicySourceResolver.MarkerFileName), """{ "id": "contoso.bimtools" }""");

        await Assert.That(OneDriveLibraries.ScanForMarker(_dir, "contoso.bimtools", depth: 3)).IsEqualTo(target);
        await Assert.That(OneDriveLibraries.ScanForMarker(_dir, "someone.else", depth: 3)).IsNull();
        await Assert.That(OneDriveLibraries.ScanForMarker(_dir, "contoso.bimtools", depth: 1)).IsNull(); // too shallow
    }

    [Test]
    [Arguments("https://git.contoso.com/bim/policy.json", "https://git.contoso.com/bim/policy.json", "URL")]
    [Arguments("http://git.contoso.com/bim/policy.json", "https://git.contoso.com/bim/policy.json", "URL (upgraded to https)")]
    [Arguments("contoso.com", "https://contoso.com/.well-known/analysetool/policy.json", "well-known URL of the domain")]
    [Arguments("source:bimtools/policy.json", "source:bimtools/policy.json", "named source")]
    public async Task What_the_user_typed_becomes_one_candidate(string input, string expectedReference, string how)
    {
        IReadOnlyList<PolicyCandidate> candidates = PolicyDiscovery.Candidates(input);
        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].Reference).IsEqualTo(expectedReference);
        await Assert.That(candidates[0].How).IsEqualTo(how);
    }

    [Test]
    public async Task A_folder_input_points_at_its_policy_json()
    {
        IReadOnlyList<PolicyCandidate> candidates = PolicyDiscovery.Candidates(@"\\server\share\analysetool");
        await Assert.That(candidates[0].Reference).IsEqualTo(Path.Combine(@"\\server\share\analysetool", "policy.json"));
        await Assert.That(PolicyDiscovery.Candidates(@"C:\bim\policy.json")[0].Reference).IsEqualTo(@"C:\bim\policy.json");
    }

    [Test]
    public async Task Membership_round_trips_through_org_json()
    {
        string profile = Path.Combine(_dir, "profile");
        OrgMembership m = new() { PolicyUrl = "https://x/policy.json", OrganizationName = "X", CachedPolicy = "{}", SigningKey = "abc" };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(m);
        OrgMembership back = Newtonsoft.Json.JsonConvert.DeserializeObject<OrgMembership>(json)!;
        await Assert.That(back.PolicyUrl).IsEqualTo("https://x/policy.json");
        await Assert.That(back.SigningKey).IsEqualTo("abc");
        await Assert.That(back.HasCachedPolicy).IsTrue();
        await Assert.That(Directory.Exists(profile)).IsFalse();
    }
}
