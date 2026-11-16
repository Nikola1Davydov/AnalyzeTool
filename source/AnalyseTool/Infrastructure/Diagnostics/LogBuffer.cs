#if DEBUG
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Infrastructure.Diagnostics;

/// <summary>
///     Debug-only logging provider that keeps the last records in memory for the «Dev» page of the dock pane
///     (<c>GetLogEntries</c>). One more provider next to the journal and the file: the levels of the <c>Logging</c>
///     section apply to it as to the others.
/// </summary>
/// <remarks>
///     The page polls the buffer through the pipeline, and the pipeline logs every call: those records are dropped here,
///     by the <c>Command</c> property of the log state, or the log would fill with the polling itself.
/// </remarks>
public sealed class LogBuffer
{
    /// <summary>How many records the buffer keeps; older ones are dropped.</summary>
    public const int Capacity = 1000;

    private static readonly HashSet<string> IgnoredCommands = new(StringComparer.OrdinalIgnoreCase) { "GetLogEntries" };

    private readonly object _lock = new();
    private readonly Queue<LogEntry> _entries = new(Capacity);
    private long _sequence;

    /// <summary>Returns the records after <paramref name="afterSequence" />, oldest first.</summary>
    public IReadOnlyList<LogEntry> GetAfter(long afterSequence)
    {
        lock (_lock)
        {
            return _entries.Where(entry => entry.Sequence > afterSequence).ToList();
        }
    }

    internal void Add(DateTimeOffset timestamp, LogLevel level, string category, string message, string? exception)
    {
        lock (_lock)
        {
            if (_entries.Count == Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(new LogEntry(++_sequence, timestamp, level, category, message, exception));
        }
    }

    internal static bool IsIgnored<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> properties)
        {
            return false;
        }

        foreach (var property in properties)
        {
            if (property is { Key: "Command", Value: string command } && IgnoredCommands.Contains(command))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>One record of <see cref="LogBuffer" />.</summary>
/// <param name="Sequence">The position in the session: grows by one per record.</param>
/// <param name="Timestamp">When the record was written.</param>
/// <param name="Level">The log level.</param>
/// <param name="Category">The logger category: usually the class that wrote the record.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception with its stack trace, if any.</param>
public sealed record LogEntry(long Sequence, DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception);

/// <summary>Writes every enabled record to <see cref="LogBuffer" />.</summary>
[ProviderAlias("LogBuffer")]
internal sealed class LogBufferProvider(LogBuffer buffer) : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new LogBufferLogger(buffer, categoryName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class LogBufferLogger(LogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return NoScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || LogBuffer.IsIgnored(state))
            {
                return;
            }

            buffer.Add(DateTimeOffset.Now, logLevel, category, formatter(state, exception), exception?.ToString());
        }
    }

    private sealed class NoScope : IDisposable
    {
        public static NoScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
#endif
