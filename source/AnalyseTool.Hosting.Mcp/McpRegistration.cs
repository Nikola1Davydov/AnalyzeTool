using System.IO;
using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Mcp.Tools;
using AnalyseTool.Hosting.Mcp.Transport;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the AnalyseTool.Hosting.Mcp module.
/// </summary>
[PublicAPI]
public static class McpRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds the MCP server: AI clients call the commands marked with <see cref="McpToolAttribute" /> over Streamable HTTP
        ///     on <c>http://localhost:&lt;port&gt;/mcp</c>.
        /// </summary>
        /// <param name="application">The application Revit passed to the add-in at startup.</param>
        /// <param name="basePort">
        ///     The port for Revit 2025; every later version adds one (2026 — <c>basePort + 1</c>), so several Revit versions run
        ///     side by side. Unique per add-in: two add-ins on one port cannot both listen. <c>Mcp:Port</c> overrides it.
        /// </param>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     On in the <c>Development</c> environment, off otherwise; <c>Mcp:Enabled</c> overrides it. The address and the
        ///     token for the client are in <c>%LOCALAPPDATA%\{application}\{Revit version}\mcp.json</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>AddRevitCommands</c> was not called before.</exception>
        /// <example>
        ///     <code>
        ///     builder.AddMcp(application, basePort: 48000);
        ///     </code>
        /// </example>
        public TBuilder AddMcp(UIControlledApplication application, int basePort)
        {
            var revitVersion = application.ControlledApplication.VersionNumber;
            var connectionFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                builder.Environment.ApplicationName,
                revitVersion,
                "mcp.json");

            return builder.AddMcp(new McpHostSettings(revitVersion, basePort, connectionFile));
        }

        /// <summary>Adds the MCP server without Revit, for tests.</summary>
        internal TBuilder AddMcp(McpHostSettings settings)
        {
            if (builder.Services.All(service => service.ServiceType != typeof(ICommandExecutor)))
            {
                throw new InvalidOperationException("Call AddRevitCommands before AddMcp: MCP tools are commands of the pipeline.");
            }

            builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));

            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton<McpToolCatalog>();
            builder.Services.AddSingleton<McpTools>();
            builder.Services.AddSingleton<McpHttpServer>();
            builder.Services.AddHostedService(services => services.GetRequiredService<McpHttpServer>());
            builder.Services.AddSingleton<IMcpEndpoint>(services => services.GetRequiredService<McpHttpServer>());

            return builder;
        }
    }
}
