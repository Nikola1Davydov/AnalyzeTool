using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Elements;
using AnalyseTool.Services.SharedParameters;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyseTool.Infrastructure;

/// <summary>
///     Registers the services of the built-in feature slices (Elements, Actions, SharedParameters).
/// </summary>
public static class ToolsRegistration
{
    /// <param name="services">The services of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Adds the services the built-in commands take through their constructors.</summary>
        /// <returns>The <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>The services hold no state: functions of a document or of a file, so one instance serves every call.</remarks>
        public IServiceCollection AddTools()
        {
            // Elements
            services.AddSingleton<DataElementsCollectorService>();
            services.AddSingleton<ModelOverviewService>();
            services.AddSingleton<LinksService>();
            services.AddSingleton<ViewsSheetsService>();
            services.AddSingleton<TypeAndWorksetService>();

            // Actions
            services.AddSingleton<ParameterWriteService>();
            services.AddSingleton<ViewActionsService>();

            // SharedParameters
            services.AddSingleton<ProjectParameterService>();
            services.AddSingleton<ParameterReportService>();
            services.AddSingleton(_ => new ParameterSetStore());

            return services;
        }
    }
}
