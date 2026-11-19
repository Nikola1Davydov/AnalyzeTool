using System.Text.Json;
using System.Text.Json.Nodes;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AnalyseTool.Hosting.Mcp.Tools;

/// <summary>
///     The <c>tools/list</c> and <c>tools/call</c> handlers: a tool call is a command call through the pipeline, with
///     <see cref="CommandSources.Mcp" /> as its source.
/// </summary>
/// <remarks>
///     Every Revit version answers on one address: each tool takes an optional <c>revit</c> (<c>"2026"</c>); a call for
///     another open Revit is sent on to it, a call without one goes to the Revit used last (<see cref="McpPeers" />).
///     <c>ListRevitVersions</c> lists the open ones.
///     A failed command is a tool result with <c>isError</c>, not a protocol error, so the AI client sees the code and the
///     message — <c>no_document</c>, <c>document_changed</c>, … — and can react. The call carries no <c>Document</c> header:
///     the document is the one active at the command's first access.
/// </remarks>
internal sealed partial class McpTools(
    McpToolCatalog catalog,
    ICommandExecutor executor,
    IOptions<JsonSerializerOptions> serializerOptions,
    ILogger<McpTools> logger,
    McpPeers peers,
    McpHostSettings settings)
{
    /// <summary>The argument every tool takes: the Revit version to run in.</summary>
    public const string RevitArgument = "revit";

    /// <summary>The tool that lists the open Revit versions.</summary>
    public const string ListVersionsTool = "ListRevitVersions";

    /// <summary>
    ///     The tool that runs any tool by its name — for a command an extension registered a moment ago, which a client
    ///     that does not refresh its list on <c>tools/list_changed</c> does not show yet.
    /// </summary>
    public const string RunCommandTool = "RunCommand";

    private static readonly Dictionary<string, JsonElement> NoArguments = [];

    public ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        var tools = catalog.Tools.Select(WithRevitArgument).Prepend(RunCommand()).Prepend(ListVersions()).ToList();
        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    }

    public async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var parameters = request.Params ?? throw new McpProtocolException("The tool call has no parameters.", McpErrorCode.InvalidParams);
        if (parameters.Name == ListVersionsTool)
        {
            return Json(peers.List().Select(peer => new { version = peer.Version, lastActive = peer.LastActive, answersHere = peers.IsSelf(peer) }).ToList());
        }

        var name = parameters.Name;
        var arguments = parameters.Arguments ?? NoArguments;
        if (name == RunCommandTool)
        {
            // RunCommand { name, payload, revit? } is the tool «name» called with «payload»
            name = arguments.TryGetValue("name", out var named) && named.ValueKind == JsonValueKind.String && named.GetString() is { Length: > 0 } given
                ? given
                : throw new McpProtocolException("RunCommand needs the name of a command, e.g. \"sheets.renumber.Renumber\".", McpErrorCode.InvalidParams);
            var inner = arguments.TryGetValue("payload", out var payloadArgument) && payloadArgument.ValueKind == JsonValueKind.Object
                ? payloadArgument.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
                : new Dictionary<string, JsonElement>();
            if (arguments.TryGetValue(RevitArgument, out var version))
            {
                inner[RevitArgument] = version;
            }

            arguments = inner;
        }

        var asked = arguments.TryGetValue(RevitArgument, out var revit) && revit.ValueKind == JsonValueKind.String ? revit.GetString() : null;
        var target = peers.Resolve(asked);
        if (target is null)
        {
            var open = string.Join(", ", peers.List().Select(peer => peer.Version).Distinct());
            return Error($"revit_not_open: Revit {asked} does not answer — {(open.Length == 0 ? "no Revit does" : $"open: {open}")}. AnalyseTool must be opened once in that Revit.");
        }

        if (!peers.IsSelf(target))
        {
            // The other Revit runs it; it gets the version, so it does not send it on again
            var forwarded = arguments.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
            forwarded[RevitArgument] = target.Version;
            var bearer = McpConnectionFile.ReadToken(settings.ConnectionFile) ?? "";
            return await peers.CallAsync(target, name, forwarded, bearer, cancellationToken).ConfigureAwait(false);
        }

        // Looked up here, not before: another Revit version may have extensions this one has not
        var entry = catalog.Find(name) ?? throw new McpProtocolException($"Unknown tool '{name}'.", McpErrorCode.InvalidParams);
        arguments = arguments.Where(pair => pair.Key != RevitArgument).ToDictionary(pair => pair.Key, pair => pair.Value);
        var payload = entry.Command.RequestType is null
            ? default
            : JsonSerializer.SerializeToElement(arguments, serializerOptions.Value);

        var progress = parameters.ProgressToken is { } token ? new ProgressSink(request.Server, token, logger) : null;
        var commandRequest = new CommandRequest(entry.Command.Name, payload, CommandSources.Mcp)
        {
            Progress = progress,
            CancellationToken = cancellationToken
        };

        var result = await executor.ExecuteAsync(commandRequest).ConfigureAwait(false);
        if (progress is not null)
        {
            // The last progress must reach the client before the result: after the result it belongs to no call
            await progress.FlushAsync().ConfigureAwait(false);
        }

        if (result.Error is { } error)
        {
            return Error($"{error.Code}: {error.Message}");
        }

        if (result.Value is McpImage image)
        {
            return new CallToolResult
            {
                Content = image.Caption is null
                    ? [ImageContentBlock.FromBytes(image.Data, image.MimeType)]
                    : [ImageContentBlock.FromBytes(image.Data, image.MimeType), new TextContentBlock { Text = image.Caption }]
            };
        }

        return Json(result.Value);
    }

    private CallToolResult Json(object? result)
    {
        var value = JsonSerializer.SerializeToElement(result, result?.GetType() ?? typeof(object), serializerOptions.Value);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = value.GetRawText() }],
            // Structured content must be an object; a list or a scalar stays text only
            StructuredContent = value.ValueKind == JsonValueKind.Object ? value : null
        };
    }

    private static CallToolResult Error(string text)
    {
        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = text }] };
    }

    /// <summary>A tool of the catalog with the optional <c>revit</c> argument in its input schema.</summary>
    private static Tool WithRevitArgument(Tool tool)
    {
        var node = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)!.AsObject();
        if (node["inputSchema"] is not JsonObject schema)
        {
            node["inputSchema"] = schema = new JsonObject { ["type"] = "object" };
        }

        if (schema["properties"] is not JsonObject properties)
        {
            schema["properties"] = properties = new JsonObject();
        }

        properties[RevitArgument] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = $"The Revit version to run in, e.g. \"2026\". Only when several Revit versions are open ({ListVersionsTool}); without it, the Revit used last."
        };
        return node.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)!;
    }

    private static Tool RunCommand()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "The command, as its tool is named: e.g. \"sheets.renumber.Renumber\"." },
                ["payload"] = new JsonObject { ["type"] = "object", ["description"] = "Its arguments, as the command's input schema has them." },
                [RevitArgument] = new JsonObject { ["type"] = "string", ["description"] = "The Revit version to run in; without it, the Revit used last." }
            },
            ["required"] = new JsonArray("name")
        };
        return new Tool
        {
            Name = RunCommandTool,
            Title = "Run a command by its name",
            Description = "Runs a command of AnalyseTool or of an extension by its name — also one a button got a moment ago (CreateExtension, " +
                          "WriteExtensionFile, BuildExtension) that your tool list does not show yet. Same result as calling its tool.",
            InputSchema = JsonSerializer.SerializeToElement(schema)
        };
    }

    private static Tool ListVersions()
    {
        return new Tool
        {
            Name = ListVersionsTool,
            Title = "Open Revit versions",
            Description = "Lists the Revit versions open with AnalyseTool on this machine: version, when it was in front last, and which one answers on this address. " +
                          "Every other tool takes \"revit\": \"<version>\" to run in one of them; without it, a call goes to the Revit used last.",
            InputSchema = JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
            Annotations = new ToolAnnotations { ReadOnlyHint = true }
        };
    }

    [LoggerMessage(LogLevel.Debug, "A progress notification could not be sent")]
    private static partial void LogProgressFailed(ILogger logger, Exception exception);

    /// <summary>
    ///     Progress of one tool call as MCP progress notifications. The command reports a fraction, so <c>total</c> is 1.
    /// </summary>
    /// <remarks>
    ///     <see cref="Report" /> must not block the command, which may be on the Revit thread; the notifications are chained,
    ///     so they go out in the order reported, and <see cref="FlushAsync" /> waits for the last one.
    /// </remarks>
    private sealed class ProgressSink(McpServer server, ProgressToken token, ILogger logger) : IProgress<ProgressInfo>
    {
        private readonly Lock _gate = new();
        private Task _last = Task.CompletedTask;

        public void Report(ProgressInfo value)
        {
            lock (_gate)
            {
                _last = _last.ContinueWith(_ => NotifyAsync(value), TaskScheduler.Default).Unwrap();
            }
        }

        public Task FlushAsync()
        {
            lock (_gate)
            {
                return _last;
            }
        }

        private async Task NotifyAsync(ProgressInfo value)
        {
            try
            {
                var progress = new ProgressNotificationValue { Progress = (float)value.Fraction, Total = 1, Message = value.Message };
                await server.NotifyProgressAsync(token, progress).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The client may be gone; progress is a courtesy, the result still goes out
                LogProgressFailed(logger, exception);
            }
        }
    }
}
