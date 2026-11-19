using System.Text.Json;
using AnalyseTool.Hosting.Notifications;
using AnalyseTool.Hosting.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     The single entry point of the pipeline for every transport: WebView2, ribbon buttons, later MCP.
/// </summary>
[PublicAPI]
public interface ICommandExecutor
{
    /// <summary>
    ///     Runs the command through the pipeline. Never throws: failures come back as <see cref="CommandResult.Error" />.
    /// </summary>
    /// <remarks>The pipeline always runs on the thread pool, never on the Revit UI thread.</remarks>
    Task<CommandResult> ExecuteAsync(CommandRequest request);
}

/// <summary>
///     The custom middleware types, in the order of <c>AddRevitCommands(c =&gt; c.Use…)</c>.
/// </summary>
internal sealed record MiddlewarePipeline(IReadOnlyList<Type> MiddlewareTypes);

/// <summary>
///     Runs <c>ExceptionHandling → middleware → command</c>.
///     ExceptionHandling is the try/catch of this class: it turns every exception into a <see cref="CommandResult" /> and shows
///     ribbon failures with <see cref="IUserNotifier" />, because a ribbon call has no window to answer to.
/// </summary>
internal sealed partial class CommandExecutor(
    CommandRegistry registry,
    MiddlewarePipeline pipeline,
    IServiceScopeFactory scopeFactory,
    IOptions<JsonSerializerOptions> serializerOptions,
    IUserNotifier notifier,
    ILogger<CommandExecutor> logger) : ICommandExecutor
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandRequest request)
    {
        return Task.Run(() => ExecuteCoreAsync(request));
    }

    private async Task<CommandResult> ExecuteCoreAsync(CommandRequest request)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = new CommandContext(registry.Find(request.Command), request, scope.ServiceProvider);

        try
        {
            if (context.Descriptor is null)
            {
                throw new CommandException(CommandErrorCodes.NotFound, $"The command '{request.Command}' is not registered.");
            }

            var value = await BuildPipeline(scope.ServiceProvider)(context).ConfigureAwait(false);
            return CommandResult.Success(value);
        }
        catch (OperationCanceledException exception) when (context.CancellationToken.IsCancellationRequested)
        {
            // The caller's choice (a closed page, a superseded query), not a problem: CommandLogging records it
            return Fail(context, CommandErrorCodes.Canceled, "The command was cancelled.", exception, LogLevel.Debug);
        }
        catch (CommandException exception)
        {
            return Fail(context, exception.Code, exception.Message, exception, LogLevel.Warning);
        }
        catch (JsonException exception)
        {
            return Fail(context, CommandErrorCodes.BadRequest, $"The payload of '{context.CommandName}' is invalid: {exception.Message}", exception, LogLevel.Warning);
        }
        catch (Exception exception)
        {
            return Fail(context, CommandErrorCodes.Failed, exception.Message, exception, LogLevel.Error);
        }
    }

    private CommandDelegate BuildPipeline(IServiceProvider services)
    {
        CommandDelegate next = context => context.Descriptor!.Invoke(context.Services, context, serializerOptions.Value);

        for (var index = pipeline.MiddlewareTypes.Count - 1; index >= 0; index--)
        {
            var middleware = (ICommandMiddleware)services.GetRequiredService(pipeline.MiddlewareTypes[index]);
            var inner = next;
            next = context => middleware.InvokeAsync(context, inner);
        }

        return next;
    }

    private CommandResult Fail(CommandContext context, string code, string message, Exception exception, LogLevel level)
    {
        if (level == LogLevel.Error)
        {
            LogUnexpectedFailure(logger, exception, context.CommandName, context.Source);
        }
        else
        {
            LogExpectedFailure(logger, level, context.CommandName, context.Source, code, message);
        }

        if (context.Source == CommandSources.Ribbon)
        {
            notifier.Error(context.CommandName, message);
        }

        return CommandResult.Failure(code, message);
    }

    [LoggerMessage(LogLevel.Error, "Command {Command} ({Source}) failed")]
    private static partial void LogUnexpectedFailure(ILogger logger, Exception exception, string command, string source);

    [LoggerMessage("Command {Command} ({Source}) refused: {Code} — {Message}")]
    private static partial void LogExpectedFailure(ILogger logger, LogLevel level, string command, string source, string code, string message);
}
