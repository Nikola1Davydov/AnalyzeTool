using AnalyseTool.Extensions;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Versions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the AnalyseTool.Extensions module.
/// </summary>
public static class ExtensionsRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds the extensions: their folders, the compiler, the loader that keeps their commands in the pipeline, and the
        ///     history of each folder. The commands of the module come with <c>FromAssemblyOf&lt;ExtensionManager&gt;()</c> in
        ///     <c>AddRevitCommands</c>.
        /// </summary>
        /// <param name="revitVersion">The year of the running Revit: <c>2025</c>, <c>2026</c>, <c>2027</c>.</param>
        /// <param name="paths">The profile; the current user's when absent (tests pass a temp folder).</param>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        public TBuilder AddExtensions(string revitVersion, ExtensionPaths? paths = null)
        {
            builder.Services.TryAddSingleton(paths ?? ExtensionPaths.ForCurrentUser());
            builder.Services.TryAddSingleton(services => new ExtensionCatalog(services.GetRequiredService<ExtensionPaths>(), revitVersion));
            builder.Services.TryAddSingleton<ExtensionStateStore>();
            builder.Services.TryAddSingleton<IExtensionHistory, LibGit2ExtensionHistory>();
            builder.Services.TryAddSingleton<ExtensionHost>();
            builder.Services.TryAddSingleton<ExtensionManager>();
            builder.Services.TryAddSingleton<ExtensionWatcher>();
            builder.Services.AddHostedService(services => services.GetRequiredService<ExtensionWatcher>());
            return builder;
        }
    }
}
