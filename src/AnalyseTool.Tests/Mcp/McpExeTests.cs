using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests.Mcp;

/// <summary>
/// Tier 2: the whole MCP path minus Revit. The published exe talks stdio to the test and the bridge
/// envelope to <see cref="FakeBridge"/>; every promise the exe makes to a client — names, schemas,
/// structured content, error text, the list_changed notification — is checked here, where #98 lived
/// for three weeks without a test able to see it.
/// </summary>
[NotInParallel("mcp-exe")] // each test spawns a process and a listener; keep them sequential
public class McpExeTests
{
    private static FakeBridge NewBridge()
    {
        FakeBridge bridge = new();
        bridge.Commands.Add(new("GetThings", "Returns things.", FakeBridge.LimitInput, FakeBridge.ObjectSchema, ReadOnly: true));
        bridge.Commands.Add(new("GetNames", "Returns names as a bare array.", FakeBridge.EmptyInput, FakeBridge.ArraySchema, ReadOnly: true));
        bridge.Commands.Add(new("acme.x.Run", "An extension command.", FakeBridge.EmptyInput, FakeBridge.ObjectSchema, Destructive: true));
        bridge.OnInvoke = (command, _) => command switch
        {
            "GetThings" => (new JObject { ["ok"] = true, ["count"] = 2 }, null),
            "GetNames" => (new JArray("a", "b"), null),
            _ => (null, new JObject { ["code"] = "command_failed", ["message"] = "InvalidOperationException: boom", ["hint"] = "Try less." }),
        };
        return bridge.Start();
    }

    [Test, Timeout(60_000)]
    public async Task Tools_list_mirrors_the_bridge_catalog(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();

        JObject list = await exe.RequestAsync("tools/list");
        JArray tools = (JArray)list["tools"]!;
        Dictionary<string, JObject> byName = tools.Cast<JObject>().ToDictionary(t => (string)t["name"]!);

        using (Assert.Multiple())
        {
            // Names are sanitized for clients that refuse dots; the description starts with the tool
            // name for clients whose search indexes descriptions only.
            await Assert.That(byName.Keys).IsEquivalentTo(new[] { "GetThings", "GetNames", "acme_x_Run" });
            await Assert.That((string)byName["GetThings"]["description"]!).StartsWith("GetThings: ");
            // outputSchema only where the answer is an object — a bare array can never be structuredContent.
            await Assert.That(byName["GetThings"]["outputSchema"]).IsNotNull();
            await Assert.That(byName["GetNames"]["outputSchema"]).IsNull();
            await Assert.That((bool)byName["GetThings"]["annotations"]!["readOnlyHint"]!).IsTrue();
            await Assert.That((bool)byName["acme_x_Run"]["annotations"]!["destructiveHint"]!).IsTrue();
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_call_returns_text_always_and_structured_content_for_objects(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        JObject things = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetThings", ["arguments"] = new JObject { ["limit"] = 5 } });
        JObject names = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetNames", ["arguments"] = new JObject() });

        using (Assert.Multiple())
        {
            await Assert.That((bool?)things["isError"] ?? false).IsFalse();
            await Assert.That((string)things["content"]![0]!["text"]!).Contains("\"count\": 2");
            await Assert.That((int)things["structuredContent"]!["count"]!).IsEqualTo(2);
            await Assert.That(names["structuredContent"]).IsNull();
            await Assert.That((string)names["content"]![0]!["text"]!).Contains("\"a\"");
        }
        // The payload reached the bridge unchanged, under the real command name.
        await Assert.That(bridge.Invocations.Select(i => i.Command)).Contains("GetThings");
        await Assert.That((int?)bridge.Invocations.First(i => i.Command == "GetThings").Payload?["limit"]).IsEqualTo(5);
    }

    [Test, Timeout(60_000)]
    public async Task An_error_arrives_as_a_code_a_message_and_a_hint(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        JObject reply = await exe.RequestAsync("tools/call", new JObject { ["name"] = "acme_x_Run", ["arguments"] = new JObject() });
        string text = (string)reply["content"]![0]!["text"]!;

        using (Assert.Multiple())
        {
            await Assert.That((bool?)reply["isError"]).IsEqualTo(true);
            await Assert.That(text).StartsWith("[command_failed] InvalidOperationException: boom");
            await Assert.That(text).Contains("Hint: Try less.");
        }
        // The sanitized name mapped back to the real command on the wire.
        await Assert.That(bridge.Invocations.Select(i => i.Command)).Contains("acme.x.Run");
    }

    [Test, Timeout(60_000)]
    public async Task An_unreachable_revit_is_an_error_not_a_crash(CancellationToken ct)
    {
        // A port nobody listens on: Revit closed, or the server switched off in Settings.
        await using McpExe exe = McpExe.Start(bridgePort: 1);
        await exe.InitializeAsync();

        JObject list = await exe.RequestAsync("tools/list");
        JObject call = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetThings", ["arguments"] = new JObject() });

        using (Assert.Multiple())
        {
            await Assert.That(((JArray)list["tools"]!).Count).IsEqualTo(0);
            await Assert.That((bool?)call["isError"]).IsEqualTo(true);
            await Assert.That((string)call["content"]![0]!["text"]!).StartsWith("[revit_unreachable]");
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_reply_with_a_new_stamp_triggers_list_changed_and_a_fresh_list(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        // What SaveAsCommand does on the host: a new command exists and the stamp moved. The reply to
        // the very next call carries the new stamp.
        bridge.Commands.Add(new("acme.x.Ping", "Freshly saved.", FakeBridge.EmptyInput, FakeBridge.ObjectSchema));
        bridge.Stamp = "1:1";
        await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetThings", ["arguments"] = new JObject() });

        JObject? notification = await exe.WaitForNotificationAsync("notifications/tools/list_changed", TimeSpan.FromSeconds(15));
        await Assert.That(notification).IsNotNull().Because($"no list_changed after a stamp change; exe stderr:\n{exe.Stderr}");

        JObject list = await exe.RequestAsync("tools/list");
        await Assert.That(((JArray)list["tools"]!).Select(t => (string)t["name"]!)).Contains("acme_x_Ping");
    }

    [Test, Timeout(60_000)]
    public async Task The_watcher_notices_a_stamp_change_with_no_call_at_all(CancellationToken ct)
    {
        // A Revit restarted underneath a running client, or a client that never calls anything: only
        // the background poll can carry the news.
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        bridge.Stamp = "7:1";
        JObject? notification = await exe.WaitForNotificationAsync("notifications/tools/list_changed", TimeSpan.FromSeconds(20));
        await Assert.That(notification).IsNotNull().Because($"the 5-second watcher never reported; exe stderr:\n{exe.Stderr}");
    }
}
