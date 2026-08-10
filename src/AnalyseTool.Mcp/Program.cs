using AnalyseTool.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// The AI client launches this exe (stdio). It forwards each tool call to the in-Revit bridge over a
// localhost TCP. Port comes from --port (default shared with the bridge via McpWire).
int port = ParsePort(args) ?? AnalyseTool.Mcp.McpWire.DefaultPort;
// The bridge refuses requests without it; Settings generates the config snippet that supplies it.
string token = ParseOption(args, "--token") ?? string.Empty;

// Startup banner on STDERR (never stdout — that's the MCP protocol channel). Shows in the AI
// client's MCP server log so it's unambiguous which build is running and which port it targets.
Console.Error.WriteLine($"[AnalyseTool.Mcp] starting — {BuildStamp()}, bridge port {port}");
if (token.Length == 0)
    Console.Error.WriteLine("[AnalyseTool.Mcp] no --token argument: Revit will reject every call. " +
                            "Copy the configuration snippet from AnalyseTool Settings → MCP server.");

RevitBridgeClient bridge = new RevitBridgeClient(port, token);

// What tools/list decided about each (sanitized) tool name, for CallTool to act on: the real Revit
// command behind it, and whether it advertised an outputSchema — a promise the call then has to keep
// with structuredContent. One entry, because both facts are decided at the same moment about the same
// tool; two parallel maps keyed alike would only be a chance to disagree.
//
// Replaced WHOLESALE at the end of a listing rather than cleared and refilled, so a call in flight
// keeps reading the previous complete map instead of one being rebuilt under it. That also means no
// concurrent collection is needed — publishing is a single reference assignment.
IReadOnlyDictionary<string, ToolBinding> toolBindings = new Dictionary<string, ToolBinding>();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// CRITICAL: stdout is the MCP protocol channel — nothing else may write to it. Drop all logging
// providers so no console logger corrupts the stream. (Diagnostics could go to stderr instead.)
builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "analysetool-revit", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithListToolsHandler(async (context, ct) =>
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
                        Description = string.IsNullOrWhiteSpace(description)
                            ? $"Runs the Revit command '{command}' (source: {source})."
                            : description,
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
                    bool declaresResult = DeclaresObjectResult(entry?[McpWire.OutputSchema]);
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

        // Published only now: until this assignment, callers still see the previous listing whole.
        toolBindings = bindings;
        return new ListToolsResult { Tools = tools };
    })
    .WithCallToolHandler(async (context, ct) =>
    {
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
            JsonNode? result = await bridge.InvokeAsync(command, payload, ct);
            string text = result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

            // The text block stays unconditionally: it is what every client can read, including the ones
            // that ignore structured output entirely.
            CallToolResult callResult = new CallToolResult { Content = { new TextContentBlock { Text = text } } };

            // Structured content ONLY where the tool promised a schema at listing time and the answer
            // really is an object. Promising a schema and then not delivering is the one way to be worse
            // than saying nothing, and a JSON array — which several of our commands return — cannot be
            // structuredContent at all.
            if (binding is { HasOutputSchema: true } && result is JsonObject resultObject)
                callResult.StructuredContent = resultObject.Deserialize<JsonElement>();

            return callResult;
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = { new TextContentBlock { Text = Describe(ex) } },
            };
        }
    });

IHost host = builder.Build();
await host.RunAsync();

return;

// ---- helpers ----

// Identifies the running build: assembly version + the exe/dll build time (changes every rebuild),
// so the log proves whether a fresh exe was deployed.
static string BuildStamp()
{
    System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
    string ver = asm.GetName().Version?.ToString() ?? "?";
    string built;
    try { built = System.IO.File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd HH:mm:ss"); }
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
/// Three cases are refused, each for its own reason:
/// <list type="bullet">
/// <item>a command that declared nothing arrives as the empty object schema
/// (<c>{"type":"object","properties":{}}</c>) — advertising that would promise structure and describe
/// none;</item>
/// <item>the free-form fallback the bridge substitutes for an oversized schema has no properties either,
/// and promising a shape it does not describe buys nothing;</item>
/// <item><b>an array-rooted schema</b> — several commands legitimately return a JSON array
/// (GetElements, GetCategoriesInRevit…), and structuredContent is defined as an OBJECT, so such a tool
/// cannot honour the promise no matter what. Its shape stays in the description and in the text block.
/// Making those commands wrap their answer in <c>{ items: [...] }</c> would fix it, at the cost of a
/// breaking change for the frontend that reads them — a separate decision, not one to smuggle in
/// here.</item>
/// </list>
/// </summary>
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

static bool DeclaresObjectResult(JsonNode? schema)
{
    if (schema is not JsonObject obj) return false;
    if (obj["type"]?.GetValue<string>() != "object") return false;
    return obj["properties"] is JsonObject properties && properties.Count > 0;
}

/// <summary>What tools/list decided about one tool name: the Revit command it maps back to, and whether
/// it advertised an outputSchema (so CallTool knows whether structuredContent is owed).</summary>
internal sealed record ToolBinding(string Command, bool HasOutputSchema);
