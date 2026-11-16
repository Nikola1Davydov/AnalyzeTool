using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.ServiceDefaults.Configuration;

/// <summary>
///     Provides extension methods for <see cref="IHostApplicationBuilder" /> to add the configuration layers.
/// </summary>
public static class ConfigurationRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds the configuration layers; a later layer overrides an earlier one:
        ///     <list type="number">
        ///         <item><c>appsettings.json</c> next to the add-in — defaults, read-only;</item>
        ///         <item><c>appsettings.{Environment}.json</c> — <c>Development</c> in debug builds;</item>
        ///         <item><c>%AppData%\{Application}\{Revit version}\usersettings.json</c> — what the user changed, written by <c>IUserSettings</c>;</item>
        ///         <item>environment variables with the <c>{APPLICATION}_</c> prefix — for debugging.</item>
        ///     </list>
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        public TBuilder AddConfigurationDefaults()
        {
            var environment = builder.Environment;

            // Absolute paths: with DisableDefaults the configuration has no base path of its own
            builder.Configuration.AddJsonFile(Path.Combine(environment.ContentRootPath, "appsettings.json"), optional: true, reloadOnChange: false);
            builder.Configuration.AddJsonFile(Path.Combine(environment.ContentRootPath, $"appsettings.{environment.EnvironmentName}.json"), optional: true, reloadOnChange: false);

            var userSettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                environment.ApplicationName,
                RevitApiContext.Application.VersionNumber,
                "usersettings.json");

            builder.AddUserSettings(userSettingsPath);

            builder.Configuration.AddEnvironmentVariables($"{environment.ApplicationName.Replace('.', '_').ToUpperInvariant()}_");

            return builder;
        }
    }
}
