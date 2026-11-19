using System.IO;
using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Lifetime;
using AnalyseTool.Hosting.Notifications;
using AnalyseTool.Hosting.Settings;
using AnalyseTool.Hosting.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the base services of the AnalyseTool.Hosting module.
/// </summary>
[PublicAPI]
public static class RevitHostingRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds <see cref="IRevitDispatcher" />, <see cref="IRevitLifetime" /> and <see cref="IUserNotifier" />.
        /// </summary>
        /// <param name="application">The application Revit passed to the add-in at startup.</param>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     Call while the add-in starts — inside <c>OnStartup</c>, before the first <c>await</c>: the dispatcher's external
        ///     event can be created only in a valid Revit API context, and <c>ApplicationInitialized</c> must be subscribed to
        ///     before Revit raises it. Both are therefore created right here, not lazily by the container.
        /// </remarks>
        public TBuilder AddRevitHosting(UIControlledApplication application)
        {
            builder.Services.AddSingleton(application);
            builder.Services.AddSingleton<IRevitDispatcher>(new RevitDispatcher());
            builder.Services.AddSingleton<IRevitLifetime>(new RevitLifetime(application.ControlledApplication));
            builder.Services.AddSingleton<IUserNotifier, TaskDialogNotifier>();

            return builder;
        }

        /// <summary>
        ///     Adds the user settings file as a configuration source and registers <see cref="IUserSettings" />.
        /// </summary>
        /// <param name="filePath">The settings file; created on the first write.</param>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>The order of configuration sources matters: sources added later override this file.</remarks>
        public TBuilder AddUserSettings(string filePath)
        {
            // The directory must exist for the file watcher behind reloadOnChange
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            builder.Configuration.AddJsonFile(filePath, optional: true, reloadOnChange: true);
            builder.Services.AddSingleton<IUserSettings>(services => new UserSettings(filePath, services.GetRequiredService<IConfiguration>()));

            return builder;
        }
    }
}
