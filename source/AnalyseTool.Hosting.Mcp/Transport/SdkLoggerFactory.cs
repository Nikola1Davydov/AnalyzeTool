using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Mcp.Transport;

/// <summary>
///     The logger factory the MCP SDK gets: only records from <see cref="McpOptions.SdkLogLevel" /> up pass to the add-in's log.
/// </summary>
/// <remarks>
///     The SDK writes several Information records per call, and the pipeline already logs the call. A filter rule cannot
///     quiet them: the configuration has a <c>Default</c> rule per provider (journal, file), and a provider rule beats a
///     category rule of any provider. So the SDK's loggers are filtered where they are made.
/// </remarks>
internal sealed class SdkLoggerFactory(ILoggerFactory inner, LogLevel minimumLevel) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(inner.CreateLogger(categoryName), minimumLevel);
    }

    public void AddProvider(ILoggerProvider provider)
    {
        inner.AddProvider(provider);
    }

    public void Dispose()
    {
        // The host owns the inner factory
    }

    private sealed class Logger(ILogger inner, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= minimumLevel && inner.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
