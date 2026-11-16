namespace AnalyseTool.Hosting.Commands.BuiltIn;

/// <summary>
///     The built-in <c>Host.*</c> commands of the module, registered by <c>AddRevitCommands</c>.
/// </summary>
/// <remarks>
///     Listed explicitly instead of scanning the module assembly: a scan would touch every module type, including those
///     built on Revit UI classes, which cannot load outside Revit (for example in unit tests).
/// </remarks>
internal static class BuiltInCommands
{
    public static IReadOnlyList<Type> Types { get; } = [typeof(GetCommands)];
}

/// <summary>Lists the registered commands — for the frontend and diagnostics.</summary>
[Command("Host.GetCommands", Description = "Lists the registered commands.")]
internal sealed class GetCommands(CommandRegistry registry) : ICommand<IReadOnlyList<CommandInfo>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CommandInfo>> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<CommandInfo> commands = registry.Commands
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(command => new CommandInfo(command.Name, command.Description, command.RequestType?.Name, command.ResponseType.Name))
            .ToList();

        return Task.FromResult(commands);
    }
}

/// <summary>An item of <c>Host.GetCommands</c>.</summary>
[PublicAPI]
public sealed record CommandInfo(string Name, string? Description, string? RequestType, string ResponseType);
