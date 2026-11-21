using System.Reflection;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Commands.BuiltIn;
using AnalyseTool.Hosting.Pipeline;
using AnalyseTool.Hosting.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the commands and the pipeline of the AnalyseTool.Hosting module.
/// </summary>
[PublicAPI]
public static class RevitCommandsRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Finds the command classes, checks them and registers them with the pipeline — as <c>AddControllers()</c>.
        /// </summary>
        /// <param name="configure">Which assemblies to scan and which middleware to add.</param>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <exception cref="CommandRegistrationException">The commands have problems: duplicate names, abstract requests, ….</exception>
        /// <exception cref="InvalidOperationException"><c>AddRevitHosting</c> was not called before.</exception>
        /// <example>
        ///     <code>
        ///     builder.AddRevitCommands(commands => commands
        ///         .FromAssemblyOf&lt;Host&gt;()
        ///         .UseCommandLogging());
        ///     </code>
        /// </example>
        public TBuilder AddRevitCommands(Action<RevitCommandsBuilder> configure)
        {
            if (builder.Services.All(service => service.ServiceType != typeof(IUserNotifier)))
            {
                throw new InvalidOperationException("Call AddRevitHosting before AddRevitCommands: the pipeline reports ribbon failures through its user notifier.");
            }

            var options = new RevitCommandsBuilder();
            configure(options);

            var warnings = new List<string>();
            var types = BuiltInCommands.Types
                .Concat(options.Types)
                .Concat(options.Assemblies.Distinct().SelectMany(assembly => CommandScanner.GetLoadableTypes(assembly, warnings)));

            var (commands, errors) = CommandScanner.Scan(types, type => type.Assembly == typeof(RevitCommandsRegistration).Assembly);
            if (errors.Count > 0)
            {
                throw new CommandRegistrationException(errors);
            }

            var registry = new CommandRegistry(commands, warnings);
            builder.Services.AddSingleton(registry);

            foreach (var command in commands)
            {
                builder.Services.AddScoped(command.CommandType);
            }

            foreach (var (type, lifetime) in options.Middlewares)
            {
                builder.Services.Add(new ServiceDescriptor(type, type, lifetime));
            }

            builder.Services.AddSingleton(new MiddlewarePipeline(options.Middlewares.Select(middleware => middleware.Type).ToList()));
            builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
            builder.Services.AddSingleton<ICommandSender, CommandSender>();
            builder.Services.AddHostedService<CommandRegistryReport>();

            return builder;
        }
    }
}

/// <summary>
///     Configures <c>AddRevitCommands</c>.
/// </summary>
[PublicAPI]
public sealed class RevitCommandsBuilder
{
    internal List<Assembly> Assemblies { get; } = [];
    internal List<Type> Types { get; } = [];
    internal List<(Type Type, ServiceLifetime Lifetime)> Middlewares { get; } = [];

    /// <summary>Scans the assembly of <typeparamref name="T" /> for command classes.</summary>
    public RevitCommandsBuilder FromAssemblyOf<T>()
    {
        return FromAssembly(typeof(T).Assembly);
    }

    /// <summary>Scans the assembly for command classes.</summary>
    public RevitCommandsBuilder FromAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
        return this;
    }

    /// <summary>Registers the listed command classes without scanning (tests, small modules).</summary>
    public RevitCommandsBuilder FromTypes(params Type[] types)
    {
        Types.AddRange(types);
        return this;
    }

    /// <summary>Adds <see cref="CommandLoggingMiddleware" />.</summary>
    public RevitCommandsBuilder UseCommandLogging()
    {
        return Use<CommandLoggingMiddleware>(ServiceLifetime.Singleton);
    }

    /// <summary>Adds <see cref="SingleFlightMiddleware" />.</summary>
    public RevitCommandsBuilder UseSingleFlight()
    {
        return Use<SingleFlightMiddleware>(ServiceLifetime.Singleton);
    }

    /// <summary>Adds <see cref="LongCallWatchdogMiddleware" />: a command that runs past 30 s (then 5 min) is noted in the log while it still runs.</summary>
    public RevitCommandsBuilder UseLongCallWatchdog()
    {
        return Use<LongCallWatchdogMiddleware>(ServiceLifetime.Singleton);
    }

    /// <summary>
    ///     Adds a custom middleware after the built-in ones, in call order. Created in the scope of each call.
    /// </summary>
    public RevitCommandsBuilder Use<TMiddleware>() where TMiddleware : class, ICommandMiddleware
    {
        return Use<TMiddleware>(ServiceLifetime.Scoped);
    }

    private RevitCommandsBuilder Use<TMiddleware>(ServiceLifetime lifetime) where TMiddleware : class, ICommandMiddleware
    {
        Middlewares.Add((typeof(TMiddleware), lifetime));
        return this;
    }
}

/// <summary>
///     The command classes have problems that would break calls; reported at startup with all of them at once.
/// </summary>
[PublicAPI]
public sealed class CommandRegistrationException(IReadOnlyList<string> errors)
    : Exception($"The commands cannot be registered:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}")
{
    /// <summary>Gets every problem found.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
///     Writes the registered commands and the scan warnings to the log when the host starts.
/// </summary>
internal sealed partial class CommandRegistryReport(CommandRegistry registry, ILogger<CommandRegistryReport> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var warning in registry.Warnings)
        {
            LogWarning(logger, warning);
        }

        LogRegistered(logger, registry.Commands.Count, string.Join(", ", registry.Commands.Select(command => command.Name).Order()));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Warning, "{Warning}")]
    private static partial void LogWarning(ILogger logger, string warning);

    [LoggerMessage(LogLevel.Information, "Registered {Count} commands: {Commands}")]
    private static partial void LogRegistered(ILogger logger, int count, string commands);
}
