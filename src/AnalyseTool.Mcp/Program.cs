using AnalyseTool.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// The AI client launches this exe (stdio). It forwards each tool call to the in-Revit bridge over a
// localhost TCP. Port comes from --port (default shared with the bridge via McpWire).
int port = ParsePort(args) ?? AnalyseTool.Mcp.McpWire.DefaultPort;

// Startup banner on STDERR (never stdout — that's the MCP protocol channel). Shows in the AI
// client's MCP server log so it's unambiguous which build is running and which port it targets.
Console.Error.WriteLine($"[AnalyseTool.Mcp] starting — {BuildStamp()}, bridge port {port}");

RevitBridgeClient bridge = new RevitBridgeClient(port);

// Maps the (sanitized) MCP tool name back to the real Revit command name. Rebuilt on every list.
ConcurrentDictionary<string, string> toolToCommand = new ConcurrentDictionary<string, string>();

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
        toolToCommand.Clear();

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

                    string toolName = ToToolName(command, toolToCommand);
                    toolToCommand[toolName] = command;

                    string? description = entry?[McpWire.Description]?.GetValue<string>();
                    bool readOnly = entry?[McpWire.ReadOnly]?.GetValue<bool>() ?? false;
                    bool destructive = entry?[McpWire.Destructive]?.GetValue<bool>() ?? false;
                    JsonNode? schema = entry?[McpWire.InputSchema];

                    tools.Add(new Tool
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
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Bridge unreachable/slow — return no tools (don't fail the whole server). Log to stderr,
            // which shows up in the AI client's MCP server log for diagnosis.
            Console.Error.WriteLine($"[AnalyseTool.Mcp] tools/list failed: {ex.Message}");
        }

        return new ListToolsResult { Tools = tools };
    })
    .WithCallToolHandler(async (context, ct) =>
    {
        string toolName = context.Params?.Name ?? string.Empty;
        string command = toolToCommand.TryGetValue(toolName, out string? mapped) ? mapped : toolName;
        JsonNode? payload = ArgumentsToPayload(context.Params?.Arguments);

        try
        {
            JsonNode? result = await bridge.InvokeAsync(command, payload, ct);
            string text = result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
            return new CallToolResult { Content = { new TextContentBlock { Text = text } } };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = { new TextContentBlock { Text = ex.Message } },
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

// MCP tool names must match ^[a-zA-Z0-9_-]+$ for most clients; our command names contain dots
// (e.g. "acme.sample.Hello"). Sanitize and keep a reverse map so CallTool recovers the real command.
static string ToToolName(string command, ConcurrentDictionary<string, string> existing)
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
