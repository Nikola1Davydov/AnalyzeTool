using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     Sends a command from C# code — typically a ribbon button.
/// </summary>
/// <remarks>
///     Does not wait for the command: a button runs inside a Revit external command, and the command itself goes through an
///     external event that could not run until the button returns: waiting would deadlock Revit.
///     Failures are shown with <c>IUserNotifier</c>.
/// </remarks>
[PublicAPI]
public interface ICommandSender
{
    /// <summary>Sends the command implemented by <typeparamref name="TCommand" />.</summary>
    /// <param name="payload">The request object; serialized with the application's JSON options.</param>
    void Send<TCommand>(object? payload = null);

    /// <summary>Sends the command by name.</summary>
    /// <param name="command">The command name.</param>
    /// <param name="payload">The request object; serialized with the application's JSON options.</param>
    void Send(string command, object? payload = null);
}

/// <inheritdoc />
internal sealed class CommandSender(
    CommandRegistry registry,
    ICommandExecutor executor,
    IOptions<JsonSerializerOptions> serializerOptions) : ICommandSender
{
    /// <inheritdoc />
    public void Send<TCommand>(object? payload = null)
    {
        Send(registry.Find(typeof(TCommand))?.Name ?? CommandNaming.GetName(typeof(TCommand)), payload);
    }

    /// <inheritdoc />
    public void Send(string command, object? payload = null)
    {
        var request = new CommandRequest(
            command,
            payload is null ? default : JsonSerializer.SerializeToElement(payload, serializerOptions.Value),
            CommandSources.Ribbon);

        // The executor never throws; it reports failures itself
        _ = executor.ExecuteAsync(request);
    }
}
