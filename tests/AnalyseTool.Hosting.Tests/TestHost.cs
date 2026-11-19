using System.Text.Json;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Notifications;
using AnalyseTool.Hosting.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Tests;

/// <summary>
///     The commands of the module without Revit: what <c>AddRevitHosting</c> would add is replaced by fakes.
/// </summary>
internal static class TestHost
{
    public static IHost Build(Action<RevitCommandsBuilder> configure)
    {
        var builder = CreateBuilder();
        builder.AddRevitCommands(configure);
        return builder.Build();
    }

    /// <summary>The builder with the services <c>AddRevitCommands</c> expects, checked as strictly as in ServiceDefaults.</summary>
    public static HostApplicationBuilder CreateBuilder()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services.AddLogging();
        builder.Services.AddSingleton<LogRecorder>();
        builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<LogRecorder>());
        builder.Services.Configure<JsonSerializerOptions>(options =>
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
        });
        builder.Services.AddSingleton<FakeNotifier>();
        builder.Services.AddSingleton<IUserNotifier>(services => services.GetRequiredService<FakeNotifier>());
        builder.Services.AddSingleton<Recorder>();
        return builder;
    }

    public static Task<CommandResult> ExecuteAsync(this IHost host, string command, object? payload = null, string source = CommandSources.WebView)
    {
        return host.ExecuteAsync(new CommandRequest(command, payload is null ? default : JsonSerializer.SerializeToElement(payload), source));
    }

    public static Task<CommandResult> ExecuteAsync(this IHost host, CommandRequest request)
    {
        return host.Services.GetRequiredService<ICommandExecutor>().ExecuteAsync(request);
    }
}

/// <summary>Keeps every log entry at <see cref="LogLevel.Information" /> and above.</summary>
public sealed class LogRecorder : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this);
    }

    public void Dispose()
    {
    }

    private sealed class Logger(LogRecorder recorder) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (recorder._entries)
            {
                recorder._entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}

public sealed class FakeNotifier : IUserNotifier
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_errors)
            {
                return _errors.ToList();
            }
        }
    }

    public void Error(string title, string message)
    {
        lock (_errors)
        {
            _errors.Add($"{title}: {message}");
        }
    }

    public void Info(string title, string message)
    {
    }
}

public sealed class FirstMiddleware(Recorder recorder) : ICommandMiddleware
{
    public Task<object?> InvokeAsync(CommandContext context, CommandDelegate next)
    {
        recorder.Add("first");
        return next(context);
    }
}

public sealed class SecondMiddleware(Recorder recorder) : ICommandMiddleware
{
    public Task<object?> InvokeAsync(CommandContext context, CommandDelegate next)
    {
        recorder.Add($"second:{context.Request.Headers.GetValueOrDefault("Document", "<none>")}");
        return next(context);
    }
}
