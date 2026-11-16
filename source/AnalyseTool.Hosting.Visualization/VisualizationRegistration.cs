using AnalyseTool.Hosting.Visualization;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the AnalyseTool.Hosting.Visualization module.
/// </summary>
[PublicAPI]
public static class VisualizationRegistration
{
    /// <param name="builder">The host application builder.</param>
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        ///     Adds <see cref="IGeometryOverlay" />: geometry drawn over the views of Revit — solids, curves, boxes, points —
        ///     without elements, transactions or undo steps.
        /// </summary>
        /// <returns>The <see cref="IHostApplicationBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     Nothing reaches Revit at startup: the DirectContext3D server is registered at the first
        ///     <see cref="IGeometryOverlay.Show" />.
        /// </remarks>
        /// <example>
        ///     <code>
        ///     builder.AddVisualization();
        ///     </code>
        /// </example>
        public IHostApplicationBuilder AddVisualization()
        {
            builder.Services.AddSingleton<IGeometryOverlay, GeometryOverlay>();
            return builder;
        }
    }
}
