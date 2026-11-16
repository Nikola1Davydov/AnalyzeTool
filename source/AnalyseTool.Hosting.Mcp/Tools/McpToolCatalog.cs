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
///     The commands opened to AI clients, as MCP tools: the scanned commands marked with <see cref="McpToolAttribute" /> and
///     the commands registered at run time whose <see cref="CommandAnnotations.ExposeToMcp" /> is set (extensions).
/// </summary>
/// <remarks>
///     Problems of the scanned commands are reported at startup all at once, as the command registry does. A command
///     registered at run time with a problem is left out and listed in <see cref="Problems" />: an extension must not stop
///     the add-in. The tools are rebuilt when the registry changes, and <see cref="Changed" /> tells the transport.
/// </remarks>
internal sealed class McpToolCatalog
{
    private static readonly JsonElement NoInputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly CommandRegistry _registry;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Lock _lock = new();
    private State _state;

    public McpToolCatalog(CommandRegistry registry, IOptions<JsonSerializerOptions> serializerOptions)
    {
        _registry = registry;
        _serializerOptions = serializerOptions.Value;
        _state = Build(throwOnScannedErrors: true);
        registry.Changed += (_, _) =>
        {
            lock (_lock)
            {
                _state = Build(throwOnScannedErrors: false);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised after the tools changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the tools in name order.</summary>
    public IList<Tool> Tools => Current.Tools;

    /// <summary>Gets why commands registered at run time were left out.</summary>
    public IReadOnlyList<string> Problems => Current.Problems;

    private State Current
    {
        get
        {
            lock (_lock)
            {
                if (_state.Version != _registry.Version)
                {
                    _state = Build(throwOnScannedErrors: false);
                }

                return _state;
            }
        }
    }

    /// <summary>Finds a tool by its exact name.</summary>
    public McpToolEntry? Find(string name)
    {
        return Current.ByName.GetValueOrDefault(name);
    }

    private State Build(bool throwOnScannedErrors)
    {
        var version = _registry.Version;
        var errors = new List<string>();
        var problems = new List<string>();
        var entries = new List<McpToolEntry>();
        foreach (var command in _registry.Commands.OrderBy(command => command.Name, StringComparer.Ordinal))
        {
            var entry = command.Annotations is { } annotations
                ? Describe(command, annotations, problems)
                : Describe(command, errors);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        if (errors.Count > 0 && throwOnScannedErrors)
        {
            throw new McpToolException(errors);
        }

        return new State(
            entries.ToDictionary(entry => entry.Tool.Name, StringComparer.Ordinal),
            entries.Select(entry => entry.Tool).ToList(),
            problems,
            version);
    }

    /// <summary>A scanned command: a tool only with <see cref="McpToolAttribute" />.</summary>
    private McpToolEntry? Describe(CommandDescriptor command, List<string> errors)
    {
        if (command.CommandType.GetCustomAttribute<McpToolAttribute>() is not { } attribute)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(command.Description))
        {
            errors.Add($"{command.CommandType.FullName}: an MCP tool needs [Command(Description = …)] — AI clients choose tools by it.");
            return null;
        }

        var inputSchema = NoInputSchema;
        if (command.RequestType is { } requestType)
        {
            inputSchema = AIJsonUtilities.CreateJsonSchema(requestType, serializerOptions: _serializerOptions);
            if (!IsObjectSchema(inputSchema))
            {
                errors.Add($"{command.CommandType.FullName}: the request type {requestType.Name} of an MCP tool must be an object with properties; MCP passes the arguments by name.");
                return null;
            }
        }

        return new McpToolEntry(CreateTool(command, inputSchema, null, attribute.Title, attribute.ReadOnly, attribute.Destructive), command);
    }

    /// <summary>A command registered at run time: a tool when its annotations say so.</summary>
    private static McpToolEntry? Describe(CommandDescriptor command, CommandAnnotations annotations, List<string> problems)
    {
        if (!annotations.ExposeToMcp)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(command.Description))
        {
            problems.Add($"'{command.Name}' ({command.Group}) is not an MCP tool: it has no description, and AI clients choose tools by it.");
            return null;
        }

        var inputSchema = annotations.InputSchema ?? NoInputSchema;
        if (!IsObjectSchema(inputSchema))
        {
            problems.Add($"'{command.Name}' ({command.Group}) is not an MCP tool: its input must be an object with properties.");
            return null;
        }

        return new McpToolEntry(CreateTool(command, inputSchema, annotations.OutputSchema, null, annotations.ReadOnly, annotations.Destructive), command);
    }

    private static Tool CreateTool(CommandDescriptor command, JsonElement inputSchema, JsonElement? outputSchema, string? title, bool readOnly, bool destructive)
    {
        return new Tool
        {
            Name = command.Name,
            Title = title,
            Description = command.Description,
            InputSchema = inputSchema,
            OutputSchema = outputSchema is { } output && IsObjectSchema(output) ? output : null,
            Annotations = new ToolAnnotations
            {
                Title = title,
                ReadOnlyHint = readOnly,
                DestructiveHint = !readOnly && destructive,
                // The model of the open document, not the internet
                OpenWorldHint = false
            }
        };
    }

    private static bool IsObjectSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type))
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

    private sealed record State(
        IReadOnlyDictionary<string, McpToolEntry> ByName,
        IList<Tool> Tools,
        IReadOnlyList<string> Problems,
        int Version);
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
