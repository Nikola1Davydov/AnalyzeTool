using System.Diagnostics;
using AnalyseTool.Hosting.Commands;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Pipeline;

/// <summary>How long a command may run before the watchdog notes it in the log, and when it notes it again.</summary>
/// <param name="Marks">The marks, in order: 30 s and 5 min by default.</param>
public sealed record LongCallWatchdogOptions(IReadOnlyList<TimeSpan> Marks)
{
    /// <summary>The default marks.</summary>
    public static readonly LongCallWatchdogOptions Default = new([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5)]);
}

/// <summary>
///     The black box of a frozen Revit (<c>UseLongCallWatchdog</c>): a command that runs past a mark is noted in the log
///     while it is STILL RUNNING. A command that holds the Revit thread freezes Revit without a trace — the log then ends
///     with the warning that names it, where a normal log line only appears after the call returns.
/// </summary>
public sealed partial class LongCallWatchdogMiddleware(ILogger<LongCallWatchdogMiddleware> logger, LongCallWatchdogOptions? options = null) : ICommandMiddleware
{
    private readonly LongCallWatchdogOptions _options = options ?? LongCallWatchdogOptions.Default;

    /// <inheritdoc />
    public async Task<object?> InvokeAsync(CommandContext context, CommandDelegate next)
    {
        using var done = new CancellationTokenSource();
        var watch = WatchAsync(context, done.Token);
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        finally
        {
            await done.CancelAsync().ConfigureAwait(false);
            await watch.ConfigureAwait(false);
        }
    }

    private async Task WatchAsync(CommandContext context, CancellationToken done)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (var mark in _options.Marks)
            {
                var wait = mark - stopwatch.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, done).ConfigureAwait(false);
                }

                LogStillRunning(logger, context.CommandName, context.Source, Math.Round(stopwatch.Elapsed.TotalSeconds));
            }
        }
        catch (OperationCanceledException)
        {
            // The command returned: nothing to report
        }
    }

    [LoggerMessage(LogLevel.Warning, "Command {Command} ({Source}) is still running after {Seconds} s — when Revit is frozen, this is what it is doing")]
    private static partial void LogStillRunning(ILogger logger, string command, string source, double seconds);
}
