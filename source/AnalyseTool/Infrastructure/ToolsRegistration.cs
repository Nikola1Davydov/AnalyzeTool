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
        public IServiceCollection AddTools()
        {
            return services;
        }
    }
}
