using System.Reflection;
using System.Text.Json;

namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     Calls the command class of a <see cref="CommandDescriptor" /> without reflection: built once at registration.
/// </summary>
public delegate Task<object?> CommandInvoker(IServiceProvider services, CommandContext context, JsonSerializerOptions serializerOptions);

/// <summary>
///     A registered command.
/// </summary>
/// <param name="Name">The command name.</param>
/// <param name="CommandType">The command class.</param>
/// <param name="RequestType">The input type, or <see langword="null" /> for <see cref="ICommand{TResponse}" />.</param>
/// <param name="ResponseType">The result type.</param>
/// <param name="Description">What the command does, from <see cref="CommandAttribute.Description" />.</param>
/// <param name="Invoke">Resolves the class from DI, binds the payload and runs it.</param>
[PublicAPI]
public sealed record CommandDescriptor(
    string Name,
    Type CommandType,
    Type? RequestType,
    Type ResponseType,
    string? Description,
    CommandInvoker Invoke)
{
    /// <summary>
    ///     Gets the group of a command registered at run time (<see cref="CommandRegistry.SetGroup" />), for example the id
    ///     of its extension; <see langword="null" /> for a scanned command.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    ///     Gets what a command registered at run time tells transports about itself. A scanned command describes itself
    ///     with attributes instead (<c>[McpTool]</c>).
    /// </summary>
    public CommandAnnotations? Annotations { get; init; }
}

/// <summary>
///     What a command registered at run time tells transports: whether AI clients see it, its hints and its schemas.
/// </summary>
/// <param name="ExposeToMcp">Whether AI clients see the command as an MCP tool.</param>
/// <param name="ReadOnly">The command only reads the model.</param>
/// <param name="Destructive">The command deletes or overwrites data.</param>
/// <param name="InputSchema">The JSON Schema of the input, when the request is not a CLR type the transport can describe.</param>
/// <param name="OutputSchema">The JSON Schema of the result, if known.</param>
[PublicAPI]
public sealed record CommandAnnotations(
    bool ExposeToMcp,
    bool ReadOnly = false,
    bool Destructive = false,
    JsonElement? InputSchema = null,
    JsonElement? OutputSchema = null);

/// <summary>
///     The naming rules of commands — the only place they live.
/// </summary>
[PublicAPI]
public static class CommandNaming
{
    /// <summary>The prefix of the module's built-in commands; a dot is allowed only in their names.</summary>
    public const string BuiltInPrefix = "Host.";

    private const string Suffix = "Command";

    /// <summary>
    ///     Returns the command name: the name from <see cref="CommandAttribute" />, otherwise the class name without the
    ///     <c>Command</c> suffix, as ASP.NET Core drops <c>Controller</c>.
    /// </summary>
    public static string GetName(Type commandType)
    {
        var explicitName = commandType.GetCustomAttribute<CommandAttribute>()?.Name;
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        var name = commandType.Name;
        return name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : name;
    }
}
