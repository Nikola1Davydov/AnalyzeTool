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
            // Plus the exe's own two, about calls rather than about Revit (GetJobResult, CancelJob).
            await Assert.That(byName.Keys).IsEquivalentTo(new[] { "GetThings", "GetNames", "acme_x_Run", "GetJobResult", "CancelJob" });
            await Assert.That((string)byName["GetJobResult"]["description"]!).StartsWith("GetJobResult: ");
            await Assert.That((string)byName["GetThings"]["description"]!).StartsWith("GetThings: ");
            // outputSchema for objects AND arrays (#111): the array-rooted refusal is history since spec 2026-07-28.
            await Assert.That(byName["GetThings"]["outputSchema"]).IsNotNull();
            await Assert.That((string)byName["GetNames"]["outputSchema"]!["type"]!).IsEqualTo("array");
            await Assert.That((bool)byName["GetThings"]["annotations"]!["readOnlyHint"]!).IsTrue();
            await Assert.That((bool)byName["acme_x_Run"]["annotations"]!["destructiveHint"]!).IsTrue();
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_call_returns_text_always_and_structured_content_for_objects_and_arrays(CancellationToken ct)
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
            // An array-rooted answer is structuredContent too (#111), matching the array outputSchema it listed under.
            await Assert.That(names["structuredContent"]!.Select(t => (string)t!)).IsEquivalentTo(new[] { "a", "b" });
            await Assert.That((string)names["content"]![0]!["text"]!).Contains("\"a\"");
        }
        // The payload reached the bridge unchanged, under the real command name.
        await Assert.That(bridge.Invocations.Select(i => i.Command)).Contains("GetThings");
        await Assert.That((int?)bridge.Invocations.First(i => i.Command == "GetThings").Payload?["limit"]).IsEqualTo(5);
    }

    [Test, Timeout(60_000)]
    public async Task Progress_frames_become_progress_notifications_when_the_client_sent_a_token(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        bridge.OnInvokeAsync = async (_, _, progress, _) =>
        {
            progress.Report((0.5, "half"));
            await Task.Delay(100);
            progress.Report((1.0, "done"));
            await Task.Delay(100);
            return (new JObject { ["ok"] = true, ["count"] = 2 }, null);
        };
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        JObject reply = await exe.RequestAsync("tools/call", new JObject
        {
            ["name"] = "GetThings",
            ["arguments"] = new JObject(),
            ["_meta"] = new JObject { ["progressToken"] = "p1" },
        });
        JObject? notification = await exe.WaitForNotificationAsync("notifications/progress", TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That((int)reply["structuredContent"]!["count"]!).IsEqualTo(2);
            await Assert.That(notification).IsNotNull();
            await Assert.That((string)notification!["params"]!["progressToken"]!).IsEqualTo("p1");
            await Assert.That((double)notification["params"]!["progress"]!).IsEqualTo(50);
            await Assert.That((string?)notification["params"]!["message"]).IsEqualTo("half");
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_cancelled_request_cancels_the_call_in_the_bridge(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        TaskCompletionSource<bool> tokenTripped = new();
        bridge.OnInvokeAsync = async (_, _, _, token) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { tokenTripped.TrySetResult(true); }
            return (null, null);
        };
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        int id = await exe.BeginRequestAsync("tools/call", new JObject { ["name"] = "GetThings", ["arguments"] = new JObject() });
        await Task.Delay(500);
        await exe.NotifyAsync("notifications/cancelled", new JObject { ["requestId"] = id, ["reason"] = "user changed their mind" });

        // The cancellation crossed both hops: the exe told the bridge which id to stop, and the
        // command's own token was tripped. (Whether the SDK still delivers a reply for a cancelled
        // request is its business, so the reply is not asserted on.)
        bool tripped = await tokenTripped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (Assert.Multiple())
        {
            await Assert.That(tripped).IsTrue();
            await Assert.That(bridge.Cancellations).IsNotEmpty();
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_slow_call_hands_back_a_job_and_its_result_is_collected_later(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        bridge.OnInvokeAsync = async (_, _, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            return (new JObject { ["ok"] = true, ["count"] = 7 }, null);
        };
        // One second of patience instead of sixty: the call outlives it, the exe hands back a handle.
        await using McpExe exe = McpExe.Start(bridge.Port, "test-token", "--handle-after", "1");
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        JObject handed = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetThings", ["arguments"] = new JObject() });
        string text = (string)handed["content"]![0]!["text"]!;
        await Assert.That((bool?)handed["isError"] ?? false).IsFalse();
        await Assert.That(text).Contains("\"status\":\"running\"");
        string jobId = (string)JObject.Parse(text.Split('\n')[0])["jobId"]!;

        // Collected on a later, separate connection — the one that asked is long closed.
        JObject? collected = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            JObject answer = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetJobResult", ["arguments"] = new JObject { ["jobId"] = jobId } });
            string answerText = (string)answer["content"]![0]!["text"]!;
            if (answerText.Contains("\"count\": 7")) { collected = answer; break; }
            await Assert.That(answerText).Contains("running");
            await Task.Delay(500);
        }
        await Assert.That(collected).IsNotNull();

        // A caller that lost the handle finds it in the listing.
        JObject listing = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetJobResult", ["arguments"] = new JObject() });
        await Assert.That((string)listing["content"]![0]!["text"]!).Contains(jobId);

        // Nothing to cancel once it is done — and the answer says so instead of pretending.
        JObject cancel = await exe.RequestAsync("tools/call", new JObject { ["name"] = "CancelJob", ["arguments"] = new JObject { ["jobId"] = jobId } });
        await Assert.That((string)cancel["content"]![0]!["text"]!).Contains("\"cancelled\":false");
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
            // No Revit, no Revit tools: only the exe's own two, which keep a way back to a job started
            // before Revit went away.
            await Assert.That(((JArray)list["tools"]!).Select(t => (string)t["name"]!)).IsEquivalentTo(new[] { "GetJobResult", "CancelJob" });
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
