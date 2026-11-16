using System.Reflection;
using System.Text.Json;
using AnalyseTool.Hosting.Commands;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace AnalyseTool.Hosting.Mcp.Tools;

/// <summary>
///     A command opened to AI clients, with its MCP description.
/// </summary>
/// <param name="Tool">What <c>tools/list</c> returns.</param>
/// <param name="Command">The command it calls.</param>
internal sealed record McpToolEntry(Tool Tool, CommandDescriptor Command);

/// <summary>
///     The commands marked with <see cref="McpToolAttribute" />, as MCP tools. Built once; every problem is reported at once,
///     as the command registry does.
/// </summary>
internal sealed class McpToolCatalog
{
    private static readonly JsonElement NoInputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly Dictionary<string, McpToolEntry> _byName;

    public McpToolCatalog(CommandRegistry registry, IOptions<JsonSerializerOptions> serializerOptions)
    {
        var errors = new List<string>();
        var entries = new List<McpToolEntry>();

        foreach (var command in registry.Commands.OrderBy(command => command.Name, StringComparer.Ordinal))
        {
            if (command.CommandType.GetCustomAttribute<McpToolAttribute>() is not { } attribute)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(command.Description))
            {
                errors.Add($"{command.CommandType.FullName}: an MCP tool needs [Command(Description = …)] — AI clients choose tools by it.");
                continue;
            }

            var inputSchema = NoInputSchema;
            if (command.RequestType is { } requestType)
            {
                inputSchema = AIJsonUtilities.CreateJsonSchema(requestType, serializerOptions: serializerOptions.Value);
                if (!IsObjectSchema(inputSchema))
                {
                    errors.Add($"{command.CommandType.FullName}: the request type {requestType.Name} of an MCP tool must be an object with properties; MCP passes the arguments by name.");
                    continue;
                }
            }

            var tool = new Tool
            {
                Name = command.Name,
                Title = attribute.Title,
                Description = command.Description,
                InputSchema = inputSchema,
                Annotations = new ToolAnnotations
                {
                    Title = attribute.Title,
                    ReadOnlyHint = attribute.ReadOnly,
                    DestructiveHint = !attribute.ReadOnly && attribute.Destructive,
                    // The model of the open document, not the internet
                    OpenWorldHint = false
                }
            };

            entries.Add(new McpToolEntry(tool, command));
        }

        if (errors.Count > 0)
        {
            throw new McpToolException(errors);
        }

        _byName = entries.ToDictionary(entry => entry.Tool.Name, StringComparer.Ordinal);
        Tools = entries.Select(entry => entry.Tool).ToList();
    }

    /// <summary>Gets the tools in name order.</summary>
    public IList<Tool> Tools { get; }

    /// <summary>Finds a tool by its exact name.</summary>
    public McpToolEntry? Find(string name)
    {
        return _byName.GetValueOrDefault(name);
    }

    private static bool IsObjectSchema(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() == "object",
            JsonValueKind.Array => type.EnumerateArray().Any(item => item.GetString() == "object"),
            _ => false
        };
    }
}

/// <summary>
///     The commands marked with <see cref="McpToolAttribute" /> have problems; reported at startup with all of them at once.
/// </summary>
[PublicAPI]
public sealed class McpToolException(IReadOnlyList<string> errors)
    : Exception($"The MCP tools cannot be registered:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}")
{
    /// <summary>Gets every problem found.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}
