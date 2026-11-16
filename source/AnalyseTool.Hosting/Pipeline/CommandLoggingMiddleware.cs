using System.Diagnostics;
using AnalyseTool.Hosting.Commands;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Pipeline;

/// <summary>
///     Logs who called which command, from where, how long it took and how it ended
///     (<c>UseCommandLogging</c>).
/// </summary>
public sealed partial class CommandLoggingMiddleware(ILogger<CommandLoggingMiddleware> logger) : ICommandMiddleware
{
    /// <inheritdoc />
    public async Task<object?> InvokeAsync(CommandContext context, CommandDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await next(context).ConfigureAwait(false);
            LogSucceeded(logger, context.CommandName, context.Source, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            LogCanceled(logger, context.CommandName, context.Source, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(logger, context.CommandName, context.Source, stopwatch.ElapsedMilliseconds, exception.Message);
            throw;
        }
    }

    [LoggerMessage(LogLevel.Information, "Command {Command} ({Source}) succeeded in {ElapsedMilliseconds} ms")]
    private static partial void LogSucceeded(ILogger logger, string command, string source, long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Information, "Command {Command} ({Source}) canceled by the caller after {ElapsedMilliseconds} ms")]
    private static partial void LogCanceled(ILogger logger, string command, string source, long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Information, "Command {Command} ({Source}) failed in {ElapsedMilliseconds} ms: {Reason}")]
    private static partial void LogFailed(ILogger logger, string command, string source, long elapsedMilliseconds, string reason);
}
