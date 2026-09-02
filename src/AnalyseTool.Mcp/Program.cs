using AnalyseTool.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// The AI client launches this exe (stdio). It forwards each tool call to the in-Revit bridge over a
// localhost TCP. Port comes from --port (default shared with the bridge via McpWire).
int port = ParsePort(args) ?? AnalyseTool.Mcp.McpWire.DefaultPort;
// The bridge refuses requests without it; Settings generates the config snippet that supplies it.
string token = ParseOption(args, "--token") ?? string.Empty;
// After this many seconds a still-running call is handed back as a job handle instead of being
// waited for (#99, #110). 0 = wait the whole invoke timeout. 40 by default: Claude Code drops a
// tool call at exactly 60 s (measured), so the handle has to be well inside that. Overridable for
// tests and for clients whose own timeout is known.
TimeSpan handleAfter = TimeSpan.FromSeconds(double.TryParse(ParseOption(args, "--handle-after"), out double seconds) ? seconds : 40);

// Startup banner on STDERR (never stdout — that's the MCP protocol channel). Shows in the AI
// client's MCP server log so it's unambiguous which build is running and which port it targets.
Console.Error.WriteLine($"[AnalyseTool.Mcp] starting — {BuildStamp()}, bridge port {port}");
if (token.Length == 0)
    Console.Error.WriteLine("[AnalyseTool.Mcp] no --token argument: Revit will reject every call. " +
                            "Copy the configuration snippet from AnalyseTool Settings → MCP server.");

RevitBridgeClient bridge = new RevitBridgeClient(port, token, handleAfter);

// What tools/list decided about each (sanitized) tool name, for CallTool to act on: the real Revit
// command behind it, and whether it advertised an outputSchema — a promise the call then has to keep
// with structuredContent. One entry, because both facts are decided at the same moment about the same
// tool; two parallel maps keyed alike would only be a chance to disagree.
//
// Replaced WHOLESALE at the end of a listing rather than cleared and refilled, so a call in flight
// keeps reading the previous complete map instead of one being rebuilt under it. That also means no
// concurrent collection is needed — publishing is a single reference assignment.
IReadOnlyDictionary<string, ToolBinding> toolBindings = new Dictionary<string, ToolBinding>();

// The catalog stamp the current tool list was built under (McpWire.Catalog). When a reply — or the
// background poll — carries a different one, the list is stale: re-list and tell the client with
// tools/list_changed. This is #100: a command saved during a session never reached tools/list,
// because the client fetches the list once, at connect, and nothing told it to fetch again.
string? listedCatalog = null;
// The server, for notifications sent outside a request (the poller). Captured from the first
// request context as a fallback in case DI does not hand it out.
McpServer? server = null;
SemaphoreSlim notifyGate = new SemaphoreSlim(1, 1);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// CRITICAL: stdout is the MCP protocol channel — nothing else may write to it. Drop all logging
// providers so no console logger corrupts the stream. (Diagnostics could go to stderr instead.)
builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "analysetool-revit", Version = "1.0.0" };
        // Declared so a client knows list_changed notifications are coming and re-lists on them.
        options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } };
    })
    .WithStdioServerTransport()
    .WithListToolsHandler(async (context, ct) =>
    {
        server ??= context.Server;
        return await BuildToolListAsync(ct);
    })
    .WithCallToolHandler(async (context, ct) =>
    {
        server ??= context.Server;
        string toolName = context.Params?.Name ?? string.Empty;
        // Unmapped names are forwarded as-is on purpose: a client may call a tool it cached from an
        // earlier session without listing again in this process, and the map only fills on tools/list.
        // Safe because the in-Revit bridge — not this process — is the access boundary: it gates every
        // invoke on the command's own registration (see McpBridgeServer.IsAvailableToAi).
        IReadOnlyDictionary<string, ToolBinding> bindings = toolBindings; // one read; the field may be replaced mid-call
        ToolBinding? binding = bindings.TryGetValue(toolName, out ToolBinding? found) ? found : null;
        string command = binding?.Command ?? toolName;
        JsonNode? payload = ArgumentsToPayload(context.Params?.Arguments);

        try
        {
            // The two tools this exe answers itself, about calls rather than about Revit: they are
            // how a caller reaches a call it could not wait for (#99) or wants stopped (#109).
            if (string.Equals(command, JobTools.GetResult, StringComparison.Ordinal))
                return await AnswerJobResultAsync(payload, ct);
            if (string.Equals(command, JobTools.Cancel, StringComparison.Ordinal))
                return await AnswerCancelAsync(payload, ct);

            // Progress: a command that reports it (IProgressAware) reaches the client as
            // notifications/progress, but only when the client asked by sending a progressToken —
            // a notification nobody correlates is noise on the wire (#108).
            ProgressToken? progressToken = context.Params?.ProgressToken;
            McpServer session = context.Server;
            IProgress<(double Fraction, string? Message)>? progress = progressToken is null ? null
                : new Progress<(double Fraction, string? Message)>(p =>
                    _ = session.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
                    {
                        Progress = (float)Math.Round(p.Fraction * 100, 1),
                        Total = 100,
                        Message = p.Message,
                    }, cancellationToken: CancellationToken.None));

            RevitBridgeClient.InvokeOutcome outcome = await bridge.InvokeAsync(command, payload, progress, ct);
            if (outcome.RunningJobId is { } jobId)
            {
                // Not an error: the command runs on in Revit; this is its handle. Said in prose AND as
                // JSON, because the tool declared no schema for this shape and a client reading only
                // structuredContent would see nothing.
                await NotifyIfCatalogChangedAsync(ct);
                return new CallToolResult
                {
                    Content = { new TextContentBlock { Text = RunningHandle(command, jobId) } },
                };
            }
            JsonNode? result = outcome.Result;
            string text = result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

            // The text block stays unconditionally: it is what every client can read, including the ones
            // that ignore structured output entirely.
            CallToolResult callResult = new CallToolResult { Content = { new TextContentBlock { Text = text } } };

            // Structured content ONLY where the tool promised a schema at listing time. Promising a
            // schema and then not delivering is the one way to be worse than saying nothing. Any JSON
            // value qualifies since spec 2026-07-28 (SEP-2106) — an array-rooted answer included, which
            // three of our tools give (#111); before that, structuredContent had to be an object.
            if (binding is { HasOutputSchema: true } && result is not null)
                callResult.StructuredContent = result.Deserialize<JsonElement>();

            // The reply carried the bridge's stamp; a SaveAsCommand reply is the very one that says
            // "the list just changed", and the client learns it before its next turn.
            await NotifyIfCatalogChangedAsync(ct);
            return callResult;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client sent notifications/cancelled: the bridge has been told (InvokeAsync does that
            // before rethrowing), so the command stops, not only the wait. The SDK may drop this reply
            // as belonging to a cancelled request; it is still the truthful one to give.
            return new CallToolResult
            {
                IsError = true,
                Content = { new TextContentBlock { Text = $"[{McpWire.Codes.Cancelled}] The call was cancelled by the client; Revit was told to stop it." } },
            };
        }
        catch (Exception ex)
        {
            await NotifyIfCatalogChangedAsync(ct);
            return new CallToolResult
            {
                IsError = true,
                Content = { new TextContentBlock { Text = Describe(ex) } },
            };
        }
    });

IHost host = builder.Build();
server ??= host.Services.GetService<McpServer>();

using CancellationTokenSource watcherCts = new CancellationTokenSource();
Task watcher = WatchCatalogAsync(watcherCts.Token);
try
{
    await host.RunAsync();
}
finally
{
    watcherCts.Cancel();
    try { await watcher; } catch { /* cancelled with the host */ }
}

return;

// ---- helpers ----

// The listing, as a function: tools/list calls it, and so does the catalog watcher when the stamp
// moved. Not static — it reads the bridge and publishes toolBindings/listedCatalog.
async Task<ListToolsResult> BuildToolListAsync(CancellationToken ct)
{
        List<Tool> tools = new List<Tool>();
        Dictionary<string, ToolBinding> bindings = new Dictionary<string, ToolBinding>();

        try
        {
            // Discover the live command set from Revit. If Revit isn't running / MCP is disabled,
            // return an empty list rather than failing the whole server; tools appear once reachable.
            JsonNode? listed = await bridge.ListCommandsAsync(ct);
            if (listed?[McpWire.Commands] is JsonArray commands)
            {
                foreach (JsonNode? entry in commands)
                {
                    string? command = entry?[McpWire.Name]?.GetValue<string>();
                    if (string.IsNullOrEmpty(command)) continue;
                    string source = entry?[McpWire.SourceField]?.GetValue<string>() ?? "core";

                    string toolName = ToToolName(command, bindings);

                    string? description = entry?[McpWire.Description]?.GetValue<string>();
                    bool readOnly = entry?[McpWire.ReadOnly]?.GetValue<bool>() ?? false;
                    bool destructive = entry?[McpWire.Destructive]?.GetValue<bool>() ?? false;
                    JsonNode? schema = entry?[McpWire.InputSchema];

                    Tool tool = new Tool
                    {
                        Name = toolName,
                        // The name is repeated at the head of the description on purpose: at least one
                        // client (claude.ai's tool_search) indexes descriptions but not names, so a tool
                        // asked for by its exact name was not found (field test 2026-09-02).
                        Description = string.IsNullOrWhiteSpace(description)
                            ? $"{toolName}: runs the Revit command '{command}' (source: {source})."
                            : $"{toolName}: {description}",
                        InputSchema = schema is not null
                            ? schema.Deserialize<JsonElement>()
                            : FreeFormObjectSchema(),
                        Annotations = new ToolAnnotations
                        {
                            Title = command,
                            ReadOnlyHint = readOnly,
                            DestructiveHint = destructive,
                        },
                    };

                    // Only when the command really declared an object-shaped result. See the helper for
                    // why an array-returning command is skipped even though it HAS a schema.
                    bool declaresResult = DeclaresResult(entry?[McpWire.OutputSchema]);
                    if (declaresResult)
                        tool.OutputSchema = entry![McpWire.OutputSchema]!.Deserialize<JsonElement>();

                    bindings[toolName] = new ToolBinding(command, declaresResult);
                    tools.Add(tool);
                }
            }
        }
        catch (Exception ex)
        {
            // Bridge unreachable/slow — return no tools (don't fail the whole server). Log to stderr,
            // which shows up in the AI client's MCP server log for diagnosis. The code is worth having
            // here too: "no tools appeared" has two common causes with opposite fixes — Revit is not
            // running (revit_unreachable) versus the client was configured without a token
            // (unauthorized) — and the bare message has been read as the wrong one before.
            Console.Error.WriteLine($"[AnalyseTool.Mcp] tools/list failed: {Describe(ex)}");
        }

    // The exe's own two tools ride on every listing, reachable or not: with Revit down they answer
    // revit_unreachable like everything else, and a client that cached them keeps a way back to a
    // job it started before Revit went away.
    foreach (Tool own in JobTools.Describe())
    {
        bindings[own.Name] = new ToolBinding(own.Name, false);
        tools.Add(own);
    }

    // Published only now: until this assignment, callers still see the previous listing whole.
    toolBindings = bindings;
    listedCatalog = bridge.LastCatalog;
    return new ListToolsResult { Tools = tools };
}

// Re-lists and notifies when the bridge's stamp differs from the one the list was built under.
// Serialized: a reply and the poller can notice the same change at the same moment, and one
// notification is what the client needs, not two.
async Task NotifyIfCatalogChangedAsync(CancellationToken ct)
{
    string? current = bridge.LastCatalog;
    if (current is null || string.Equals(current, listedCatalog, StringComparison.Ordinal)) return;
    if (!await notifyGate.WaitAsync(0, ct)) return; // someone else is already on it
    try
    {
        if (string.Equals(bridge.LastCatalog, listedCatalog, StringComparison.Ordinal)) return;
        // Who to tell, BEFORE re-listing: re-listing records the new stamp, and with nobody to notify
        // that would silently swallow the change — the session that appears later would never hear
        // of it. Leaving the list stale keeps the difference alive for the next tick.
        McpServer? target = server;
        if (target is null)
        {
            Console.Error.WriteLine("[AnalyseTool.Mcp] tool set changed, but no session to notify yet");
            return;
        }
        await BuildToolListAsync(ct);
        await target.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, ct);
        Console.Error.WriteLine($"[AnalyseTool.Mcp] tool set changed ({listedCatalog}) — sent tools/list_changed");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[AnalyseTool.Mcp] list_changed failed: {ex.Message}");
    }
    finally
    {
        notifyGate.Release();
    }
}

// GetJobResult: the stored outcome of a call, rendered as the call itself would have been.
async Task<CallToolResult> AnswerJobResultAsync(JsonNode? payload, CancellationToken ct)
{
    string? jobId = payload?[JobTools.JobIdField]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(jobId))
    {
        // No id: the caller lost its handle (a client whose own timeout swallowed the reply, most
        // likely). The recent calls, newest first, so it can pick the one it meant.
        JsonNode? jobs = await bridge.ListJobsAsync(ct);
        string listing = jobs?[McpWire.Jobs]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "[]";
        return new CallToolResult
        {
            Content = { new TextContentBlock { Text = $"Recent calls, newest first (pass one id as jobId to collect it):\n{listing}" } },
        };
    }
    JsonNode? job = await bridge.GetJobResultAsync(jobId, ct);
    string status = job?[McpWire.JobStatus]?.GetValue<string>() ?? McpWire.JobStates.Unknown;
    string? command = job?[McpWire.JobCommand]?.GetValue<string>();
    double seconds = job?[McpWire.JobSeconds]?.GetValue<double>() ?? 0;
    switch (status)
    {
        case McpWire.JobStates.Done:
            return new CallToolResult
            {
                Content = { new TextContentBlock { Text = job![McpWire.JobResult]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null" } },
            };
        case McpWire.JobStates.Running:
            return new CallToolResult
            {
                Content = { new TextContentBlock { Text = $"{{\"status\":\"running\",\"jobId\":\"{jobId}\",\"command\":\"{command}\",\"seconds\":{seconds}}}\n" +
                    $"'{command}' is still running in Revit ({seconds:0}s so far). Call GetJobResult again later, or CancelJob to stop it." } },
            };
        case McpWire.JobStates.Failed:
        case McpWire.JobStates.Cancelled:
        {
            JsonNode? error = job?[McpWire.JobError];
            string code = error?[McpWire.ErrorCode]?.GetValue<string>() ?? McpWire.Codes.CommandFailed;
            string message = error?[McpWire.ErrorMessage]?.GetValue<string>() ?? $"'{command}' {status}.";
            string? hint = error?[McpWire.ErrorHint]?.GetValue<string>();
            return Error(string.IsNullOrWhiteSpace(hint) ? $"[{code}] {message}" : $"[{code}] {message}\nHint: {hint}");
        }
        default:
            return Error($"[{McpWire.JobStates.Unknown}] No call with jobId \"{jobId}\" — it never ran, or its result is older than the retention window (one hour).");
    }

    static CallToolResult Error(string text) => new() { IsError = true, Content = { new TextContentBlock { Text = text } } };
}

async Task<CallToolResult> AnswerCancelAsync(JsonNode? payload, CancellationToken ct)
{
    string? jobId = payload?[JobTools.JobIdField]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(jobId))
        return new CallToolResult { IsError = true, Content = { new TextContentBlock { Text = $"[{McpWire.Codes.InvalidArguments}] jobId is required." } } };
    bool cancelled = await bridge.CancelJobAsync(jobId, ct);
    return new CallToolResult
    {
        Content = { new TextContentBlock { Text = cancelled
            ? $"{{\"cancelled\":true}}\nRevit was told to stop job \"{jobId}\". Its final state will be 'cancelled' — or 'done' if it finished first."
            : $"{{\"cancelled\":false}}\nNothing to cancel: job \"{jobId}\" is not running (finished, unknown, or already cancelled)." } },
    };
}

static string RunningHandle(string command, string jobId) =>
    $"{{\"status\":\"running\",\"jobId\":\"{jobId}\",\"command\":\"{command}\"}}\n" +
    $"'{command}' is still running in Revit and was not waited for any longer. It keeps running; " +
    $"call GetJobResult with jobId \"{jobId}\" to collect its result (kept for one hour), GetQueueStatus to see " +
    "it running, or CancelJob to stop it. Do not start the same command again meanwhile.";

// The watcher for changes no reply will ever carry: a client that connected while Revit was closed
// holds an empty list and has nothing to call, so no reply would ever bring it the stamp; and a Revit
// restarted underneath a running client comes back with a fresh catalog. A "version" request is a
// stamp and nothing else, so asking every few seconds costs nothing measurable.
async Task WatchCatalogAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            await bridge.GetCatalogAsync(ct); // updates bridge.LastCatalog when Revit answers
            await NotifyIfCatalogChangedAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // Revit not there: nothing to compare against; try again next tick.
        }
    }
}

// Identifies the running build: assembly version + the exe/dll build time (changes every rebuild),
// so the log proves whether a fresh exe was deployed.
static string BuildStamp()
{
    System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
    string ver = asm.GetName().Version?.ToString() ?? "?";
    // Single-file publish leaves Assembly.Location empty; the process path is the bundle itself,
    // whose timestamp is the build time we want to show.
    string? path = string.IsNullOrEmpty(asm.Location) ? Environment.ProcessPath : asm.Location;
    string built;
    try { built = System.IO.File.GetLastWriteTime(path!).ToString("yyyy-MM-dd HH:mm:ss"); }
    catch { built = "?"; }
    return $"v{ver} built {built}";
}

static int? ParsePort(string[] args)
{
    for (int i = 0; i < args.Length; i++)
    {
        if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
            return p;
        if (args[i].StartsWith("--port=", StringComparison.Ordinal) &&
            int.TryParse(args[i]["--port=".Length..], out int p2))
            return p2;
    }
    return null;
}

/// <summary>Reads "--name value" or "--name=value" from the command line.</summary>
static string? ParseOption(string[] args, string name)
{
    string inline = name + "=";
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == name && i + 1 < args.Length) return args[i + 1];
        if (args[i].StartsWith(inline, StringComparison.Ordinal)) return args[i][inline.Length..];
    }
    return null;
}

// MCP tool names must match ^[a-zA-Z0-9_-]+$ for most clients; our command names contain dots
// (e.g. "acme.sample.Hello"). Sanitize and keep a reverse map so CallTool recovers the real command.
static string ToToolName(string command, IReadOnlyDictionary<string, ToolBinding> existing)
{
    string baseName = Regex.Replace(command, "[^a-zA-Z0-9_-]", "_");
    if (baseName.Length > 64) baseName = baseName[..64];

    string name = baseName;
    int suffix = 1;
    while (existing.ContainsKey(name))
        name = $"{baseName}_{suffix++}";
    return name;
}

static JsonNode? ArgumentsToPayload(IDictionary<string, JsonElement>? arguments)
{
    if (arguments == null || arguments.Count == 0) return null;

    JsonObject payload = new JsonObject();
    foreach (KeyValuePair<string, JsonElement> kv in arguments)
        payload[kv.Key] = JsonNode.Parse(kv.Value.GetRawText());
    return payload;
}

static JsonElement FreeFormObjectSchema()
    => JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>(),
        ["additionalProperties"] = true,
    });

/// <summary>
/// Whether a command's declared result schema can be advertised as a tool's outputSchema.
///
/// Two cases are refused, each for its own reason:
/// <list type="bullet">
/// <item>a command that declared nothing arrives as the empty object schema
/// (<c>{"type":"object","properties":{}}</c>) — advertising that would promise structure and describe
/// none;</item>
/// <item>the free-form fallback the bridge substitutes for an oversized schema has no properties either,
/// and promising a shape it does not describe buys nothing.</item>
/// </list>
/// A third refusal — an array-rooted schema — is history: until spec 2026-07-28 structuredContent had
/// to be an object, so GetCategoriesInRevit, GetCadImports and GetWarningsInRevit answered as text
/// only. SEP-2106 lifted that, and the SDK's Tool.OutputSchema says the root need not be an object
/// any more (#111). An array schema with items is a real promise and is kept.
/// </summary>
static bool DeclaresResult(JsonNode? schema)
{
    if (schema is not JsonObject obj) return false;
    string? type = obj["type"]?.GetValue<string>();
    if (type == "array") return obj["items"] is not null;
    if (type != "object") return false;
    return obj["properties"] is JsonObject properties && properties.Count > 0;
}

/// <summary>
/// Renders a failure for the agent. The code goes FIRST, in brackets, on its own line: an MCP tool error
/// is a text block, so the code has to travel inside the text, and a fixed leading token is something a
/// reader can branch on where a sentence is not. The hint follows only when there is one — a line that
/// restates the message teaches the reader to skip hints.
/// </summary>
static string Describe(Exception ex)
{
    if (ex is not BridgeException bridge)
        return $"[{McpWire.Codes.CommandFailed}] {ex.Message}";

    string text = $"[{bridge.Code}] {bridge.Message}";
    return string.IsNullOrWhiteSpace(bridge.Hint) ? text : $"{text}\nHint: {bridge.Hint}";
}

/// <summary>
/// The exe's own tools, about calls rather than about Revit. Named like the commands so a caller does
/// not need to know which process answers; the names cannot collide with a Revit command, which
/// would be listed under a sanitized name that never equals these (a registration named
/// GetJobResult would be shadowed — acceptable, and unlikely).
/// </summary>
static class JobTools
{
    public const string GetResult = "GetJobResult";
    public const string Cancel = "CancelJob";
    public const string JobIdField = "jobId";

    public static IEnumerable<Tool> Describe()
    {
        JsonElement input = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                [JobIdField] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The jobId a running call handed back (or that a timeout hint named). " +
                                       "GetJobResult without it lists the recent calls instead.",
                },
            },
        });
        yield return new Tool
        {
            Name = GetResult,
            Description = $"{GetResult}: Collects the result of a Revit command that was handed back as a running job " +
                          "(a call answering { status: \"running\", jobId }) or whose call timed out. Returns the command's " +
                          "own result once it finished; while it runs, { status: \"running\", seconds } — call again later. " +
                          "Without jobId, lists the recent calls (id, status, command, seconds) — use it when your own " +
                          "call timed out before the handle arrived. Results are kept for one hour. Read-only, answers " +
                          "instantly even while Revit is busy.",
            InputSchema = input,
            Annotations = new ToolAnnotations { Title = GetResult, ReadOnlyHint = true },
        };
        yield return new Tool
        {
            Name = Cancel,
            Description = $"{Cancel}: Stops a running Revit command by the jobId a call handed back. The command sees its " +
                          "cancellation token; work already committed stays. Returns { cancelled: true } when it was running. " +
                          "Answers instantly even while Revit is busy.",
            InputSchema = input,
            Annotations = new ToolAnnotations { Title = Cancel, ReadOnlyHint = false, DestructiveHint = false },
        };
    }
}

/// <summary>What tools/list decided about one tool name: the Revit command it maps back to, and whether
/// it advertised an outputSchema (so CallTool knows whether structuredContent is owed).</summary>
internal sealed record ToolBinding(string Command, bool HasOutputSchema);
