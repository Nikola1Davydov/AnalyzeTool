using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Policy;
using AnalyseTool.Core.Common.Telemetry;
using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests;

/// <summary>
/// Phases 4 and 5: an extension (and Tools) reads its policy section through the Sdk without seeing
/// Core; managed AI providers come from the policy with keys from the environment; telemetry is off
/// without a policy, inventory-only by default, and never carries a payload.
/// </summary>
public class PolicyPhase45Tests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-policy45-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- 4: Sdk accessor -------------------------------------------------------------------------

    [Test]
    [NotInParallel("HostPolicy")]
    public async Task An_extension_reads_its_own_section_through_the_sdk_and_gets_null_when_absent()
    {
        try
        {
            HostPolicy.RegisterReader(section => section == "acme.standards" ? """{ "server": "https://std.acme.local", "strict": true }""" : null);

            await Assert.That(HostPolicy.GetSectionJson("acme.standards")).IsNotNull();
            var typed = HostPolicy.GetSection<AcmeSection>("acme.standards");
            await Assert.That(typed!.Server).IsEqualTo("https://std.acme.local");
            await Assert.That(typed.Strict).IsTrue();
            await Assert.That(HostPolicy.GetSection<AcmeSection>("other")).IsNull();

            HostPolicy.RegisterReader(_ => throw new InvalidOperationException("boom"));
            await Assert.That(HostPolicy.GetSectionJson("acme.standards")).IsNull(); // never throws
        }
        finally
        {
            CoreServices.RegisterSdkHooks();
        }
    }

    private sealed class AcmeSection
    {
        public string? Server { get; set; }
        public bool Strict { get; set; }
    }

    [Test]
    public async Task A_policy_document_answers_typed_raw_and_unknown_sections_as_json()
    {
        PolicyDocument doc = Newtonsoft.Json.JsonConvert.DeserializeObject<PolicyDocument>("""
            { "version": 1, "mcp": { "enabled": false }, "ai": { "allowUserProviders": false }, "acme.standards": { "x": 1 } }
            """)!;

        await Assert.That(JObject.Parse(doc.SectionJson("mcp")!)["enabled"]!.Value<bool>()).IsFalse();
        await Assert.That(JObject.Parse(doc.SectionJson("ai")!)["allowUserProviders"]!.Value<bool>()).IsFalse();
        await Assert.That(JObject.Parse(doc.SectionJson("acme.standards")!)["x"]!.Value<int>()).IsEqualTo(1);
        await Assert.That(doc.SectionJson("codeExecution")).IsNull();
        await Assert.That(doc.SectionJson("")).IsNull();

        // the merge carries the raw sections like the typed ones: machine over organization
        PolicyDocument machine = new() { Ai = JObject.Parse("""{ "allowUserProviders": true }""") };
        PolicyDocument org = new() { Ai = JObject.Parse("""{ "allowUserProviders": false }"""), Telemetry = JObject.Parse("""{ "sink": "x" }""") };
        PolicyDocument merged = PolicyStore.Merge(machine, org);
        await Assert.That(merged.Ai!["allowUserProviders"]!.Value<bool>()).IsTrue();
        await Assert.That(merged.Telemetry!["sink"]!.Value<string>()).IsEqualTo("x");
    }

    // ---- 4: managed AI providers (Tools via the Sdk) ---------------------------------------------

    [Test]
    [NotInParallel("HostPolicy")]
    public async Task Managed_ai_providers_come_from_the_policy_with_keys_from_the_environment_and_cannot_be_edited()
    {
        Environment.SetEnvironmentVariable("ANALYSETOOL_TEST_KEY", "sk-from-gpo");
        try
        {
            HostPolicy.RegisterReader(section => section == "ai" ? """
                { "providers": [ { "id": "company-gateway", "name": "Company AI", "baseUrl": "https://ai.company.local/v1/", "apiKeyEnv": "ANALYSETOOL_TEST_KEY" },
                                 { "id": "local-ollama", "type": "ollama", "baseUrl": "http://localhost:11434" } ],
                  "allowUserProviders": false }
                """ : null);

            IReadOnlyList<AiProvider> managed = AiPolicy.ManagedProviders();
            await Assert.That(managed.Count).IsEqualTo(2);
            await Assert.That(managed[0].Managed).IsTrue();
            await Assert.That(managed[0].BaseUrl).IsEqualTo("https://ai.company.local/v1");
            await Assert.That(managed[0].Type).IsEqualTo(AiProviderType.OpenAiCompatible);
            await Assert.That(managed[1].Type).IsEqualTo(AiProviderType.Ollama);
            await Assert.That(AiProviderRegistry.GetApiKey(managed[0])).IsEqualTo("sk-from-gpo");
            await Assert.That(AiProviderRegistry.GetApiKey(managed[1])).IsNull();

            await Assert.That(AiProviderRegistry.Get("company-gateway")!.Managed).IsTrue();
            await Assert.That(AiProviderRegistry.All().Select(p => p.Id)).Contains("company-gateway");
            await Assert.That(AiProviderRegistry.All()[0].Id).IsEqualTo(AiProviderRegistry.OllamaId);
            await Assert.That(AiPolicy.UserProvidersAllowed).IsFalse();
            await Assert.That(AiProviderRegistry.Refusal("company-gateway")!).Contains("managed by your organization");
            await Assert.That(AiProviderRegistry.Refusal(null)).IsNotNull(); // user providers switched off
            await Assert.That(() => { AiProviderRegistry.Save(null, "Mine", "https://x/v1", "k", null); }).Throws<InvalidOperationException>();
            await Assert.That(() => { AiProviderRegistry.Delete("company-gateway"); }).Throws<InvalidOperationException>();

            // an endpoint that is not https (and not local), or a key variable outside the plugin's prefix,
            // is dropped: a policy must not be able to ship OPENAI_API_KEY to an arbitrary host
            HostPolicy.RegisterReader(section => section == "ai" ? """
                { "providers": [ { "id": "evil", "baseUrl": "http://attacker.example/v1", "apiKeyEnv": "ANALYSETOOL_X" },
                                 { "id": "leak", "baseUrl": "https://ok.example/v1", "apiKeyEnv": "OPENAI_API_KEY" },
                                 { "id": "local", "baseUrl": "http://127.0.0.1:1234/v1" } ] }
                """ : null);
            await Assert.That(AiPolicy.ManagedProviders().Select(p => p.Id)).IsEquivalentTo(new[] { "local" });

            // no policy: nothing managed, users may add their own
            HostPolicy.RegisterReader(_ => null);
            await Assert.That(AiPolicy.ManagedProviders()).IsEmpty();
            await Assert.That(AiPolicy.UserProvidersAllowed).IsTrue();
            await Assert.That(AiProviderRegistry.Refusal(null)).IsNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANALYSETOOL_TEST_KEY", null);
            CoreServices.RegisterSdkHooks();
        }
    }

    // ---- 5: telemetry ----------------------------------------------------------------------------

    [Test]
    public async Task Telemetry_is_off_without_a_sink_and_inventory_only_without_an_events_list()
    {
        await Assert.That(new TelemetryPolicy().Allows("inventory")).IsFalse();
        TelemetryPolicy sinkOnly = new() { Sink = "https://otel.company.local/ingest" };
        await Assert.That(sinkOnly.Allows("inventory")).IsTrue();
        await Assert.That(sinkOnly.Allows("command")).IsFalse();
        await Assert.That(sinkOnly.Allows("ai")).IsFalse();
        TelemetryPolicy usage = new() { Sink = "x", Events = ["command", "AI"] };
        await Assert.That(usage.Allows("ai")).IsTrue();
        await Assert.That(usage.Allows("inventory")).IsFalse();
    }

    [Test]
    public async Task An_event_line_is_json_with_a_seat_hash_by_default_and_names_only_with_identity_user()
    {
        TelemetryPolicy hashed = new() { Sink = "x" };
        Dictionary<string, object?> props = new() { ["command"] = "GetElements", ["durationMs"] = 42L, ["outcome"] = "ok" };
        JObject line = JObject.Parse(TelemetryHub.BuildLine("command", props, hashed, DateTimeOffset.UnixEpoch, "alice", "PC1"));

        await Assert.That(line["event"]!.Value<string>()).IsEqualTo("command");
        await Assert.That(line["command"]!.Value<string>()).IsEqualTo("GetElements");
        await Assert.That(line["durationMs"]!.Value<long>()).IsEqualTo(42L);
        await Assert.That(line["seat"]!.Value<string>()!.Length).IsEqualTo(16);
        await Assert.That(line["user"]).IsNull();
        await Assert.That(line["plugin"]).IsNotNull();
        await Assert.That(TelemetryHub.SeatHash("alice", "PC1")).IsEqualTo(TelemetryHub.SeatHash("Alice", "pc1")); // stable, case-insensitive

        JObject named = JObject.Parse(TelemetryHub.BuildLine("command", props, new TelemetryPolicy { Sink = "x", Identity = "user" }, DateTimeOffset.UnixEpoch, "alice", "PC1"));
        await Assert.That(named["user"]!.Value<string>()).IsEqualTo("alice");
        await Assert.That(named["seat"]).IsNull();
    }

    [Test]
    [NotInParallel("PolicyStore")]
    public async Task Nothing_is_emitted_without_a_policy_and_a_command_event_never_carries_the_payload()
    {
        string machine = Path.Combine(_dir, "policy.json");
        string sinkDir = Path.Combine(_dir, "sink");
        try
        {
            // no policy at all → no event, no recent line
            PolicyStore.OverridePathForTests(Path.Combine(_dir, "absent.json"));
            PolicyStore.OverrideMembershipForTests(() => null);
            int before = TelemetryHub.RecentEvents().Count;
            TelemetryEvents.Command("SetDataToParameters", "webview2", 10, "ok");
            await Assert.That(TelemetryHub.RecentEvents().Count).IsEqualTo(before);
            await Assert.That(TelemetryHub.IsEnabled("command")).IsFalse();

            // a policy with a folder sink and the command kind listed
            File.WriteAllText(machine, $$"""
                { "version": 1, "telemetry": { "sink": {{Newtonsoft.Json.JsonConvert.ToString(sinkDir)}}, "events": ["command"] } }
                """);
            PolicyStore.OverridePathForTests(machine); // point the store at the file just written
            await Assert.That(TelemetryHub.IsEnabled("command")).IsTrue();
            await Assert.That(TelemetryHub.IsEnabled("inventory")).IsFalse(); // not listed → not sent

            const string secretPayload = "Kommentare=streng geheim";
            TelemetryEvents.Command("SetDataToParameters", "mcp", 123, "error:InvalidOperationException");
            string last = TelemetryHub.RecentEvents().Last();
            await Assert.That(last).Contains("SetDataToParameters");
            await Assert.That(last).Contains("error:InvalidOperationException");
            await Assert.That(last).DoesNotContain(secretPayload);
            await Assert.That(JObject.Parse(last).Properties().Select(p => p.Name))
                .IsEquivalentTo(new[] { "@t", "event", "plugin", "revit", "seat", "command", "source", "durationMs", "outcome" });

            TelemetryHub.Flush();
            string[] files = Directory.GetFiles(sinkDir, "*.jsonl");
            await Assert.That(files.Length).IsEqualTo(1);
            foreach (string l in File.ReadAllLines(files[0]))
                await Assert.That(JObject.Parse(l)["event"]).IsNotNull(); // every line is valid JSON
        }
        finally
        {
            PolicyStore.OverrideMembershipForTests(null);
            PolicyStore.OverridePathForTests(null);
        }
    }

    [Test]
    public async Task The_file_sink_appends_one_file_per_seat_and_day()
    {
        string file = TelemetryHub.WriteLines(_dir, ["{\"a\":1}", "{\"b\":2}"], new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));
        TelemetryHub.WriteLines(_dir, ["{\"c\":3}"], new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(Path.GetFileName(file)).EndsWith("-2026-09-20.jsonl");
        await Assert.That(File.ReadAllLines(file).Length).IsEqualTo(3);
    }
}
