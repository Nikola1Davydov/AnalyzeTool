using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AnalyseTool.ServiceDefaults.Diagnostics;
using Nice3point.Revit.Logging;
using Serilog;

namespace AnalyseTool.ServiceDefaults.Logging;

/// <summary>
///     Provides extension methods for <see cref="IHostApplicationBuilder" /> to add the application logging defaults.
/// </summary>
/// <example>
///     <code lang="csharp">
///         public partial class Class(ILogger&lt;Class&gt; logger)
///         {
///             private void Execute()
///             {
///                 LogMessage(logger);
///             }
///
///             [LoggerMessage(LogLevel.Information, "Message")]
///             private static partial void LogMessage(ILogger&lt;Class&gt; logger);
///         }
///     </code>
/// </example>
public static class LoggingRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds the default logging providers to the specified <see cref="IHostApplicationBuilder" />.
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     The Revit journal keeps the whole session and may be sent to Autodesk with an error report,
        ///     so release builds write only warnings and errors there.
        ///     The details for support go to the file log instead: <c>%LOCALAPPDATA%\{Application}\logs\{Revit version}\log-{date}.txt</c>.
        /// </remarks>
        public TBuilder AddLoggingDefaults()
        {
            var journalLevel = builder.Environment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning;

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = nameof(LogLevel.Debug),
                ["Logging:RevitJournal:LogLevel:Default"] = journalLevel.ToString(),
                ["Logging:Serilog:LogLevel:Default"] = nameof(LogLevel.Information)
            });

            builder.Logging.AddDebug();
            builder.Logging.AddRevitJournal(RevitApiContext.Application, options => options.ApplicationName = builder.Environment.ApplicationName);
            builder.AddFileLog();

            // After the providers: AddSerilog adds its own Trace filter, and a later filter rule wins over an earlier one
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

            builder.Services.AddHostedService<AppDomainExceptionsHandler>();
            builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

            return builder;
        }

        /// <summary>
        ///     Adds the daily rolling file log.
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     Serilog is one more provider (<c>AddSerilog</c>), not the whole logging (<c>UseSerilog</c>), which would cut the Revit journal off.
        ///     Its level comes from <c>Logging:Serilog</c> like the other providers. The file is shared:
        ///     two Revit sessions of the same version write to it together.
        /// </remarks>
        public TBuilder AddFileLog()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                builder.Environment.ApplicationName,
                "logs",
                RevitApiContext.Application.VersionNumber);

            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(
                    Path.Combine(directory, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            builder.Logging.AddSerilog(logger, dispose: true);

            return builder;
        }
    }
}
