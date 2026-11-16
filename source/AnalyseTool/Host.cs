using System.Diagnostics;
using System.Reflection;
using Autodesk.Revit.UI;
using AnalyseTool.Infrastructure;
using AnalyseTool.Services.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#if DEBUG
using AnalyseTool.Infrastructure.Diagnostics;
#endif
using Path = System.IO.Path;

namespace AnalyseTool;

/// <summary>
///     Provides a host for the application's services and manages their lifetimes.
/// </summary>
public static partial class Host
{
    private static IHost? _host;

    /// <summary>
    ///     Registers the add-in services and starts the host.
    /// </summary>
    /// <param name="application">The application Revit passed to the add-in at startup.</param>
    /// <returns>A task that represents the asynchronous host startup operation.</returns>
    public static async Task StartAsync(UIControlledApplication application)
    {
        var stopwatch = Stopwatch.StartNew();

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "AnalyseTool",
            ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
#if DEBUG
            EnvironmentName = Environments.Development,
#else
            EnvironmentName = Environments.Production,
#endif
            DisableDefaults = true
        });

        //Service defaults
        builder.AddServiceDefaults();

        //AnalyseTool.Hosting: dispatcher, Revit lifetime, notifications. Before the first await — see AddRevitHosting
        builder.AddRevitHosting(application);

        //Commands and their pipeline; the document rule is the add-in's own (ActiveDocument, DocumentCheckMiddleware)
        builder.Services.AddScoped<ActiveDocument>();
        //The built-in feature slices: Elements, Actions, SharedParameters
        builder.Services.AddTools();
        //AnalyseTool.Hosting.Scripting: ExecuteCode runs C# inside Revit — full trust, on in Debug, Scripting:Enabled otherwise
        builder.AddRevitCommands(commands => commands
            .FromAssemblyOf<Application>()
            .AddScripting(builder)
            .UseCommandLogging()
            .Use<DocumentCheckMiddleware>());

        //AnalyseTool.Hosting.WebView: windows and the dock pane with the ClientApp pages
        builder.AddWebView(application, webView => webView
            .DockPane(new Guid("6DFB18B8-31A9-4DC5-8FAE-6BE296CBA6AD"), "AnalyseTool", "/dock")
            .AllowAnyNavigation());

        //AnalyseTool.Hosting.Mcp: AI clients call the commands marked with [McpTool]; on in Debug, Mcp:Enabled otherwise
        builder.AddMcp(application, basePort: 48076);

        //AnalyseTool.Hosting.Visualization: geometry drawn over the views (ShowGeometry), nothing added to the model
        builder.AddVisualization();

#if DEBUG
        builder.Services.AddHostedService<StartupProbe>();
        builder.Services.Configure<DevOptions>(builder.Configuration.GetSection(DevOptions.SectionName));
        // The «Dev» page of the dock pane reads the log from memory; the levels of the Logging section apply here too
        builder.Services.AddSingleton<LogBuffer>();
        builder.Services.AddSingleton<ILoggerProvider, LogBufferProvider>();
#endif

        _host = builder.Build();
        await _host.StartAsync();

        LogHostStarted(_host.Services.GetRequiredService<ILogger<Application>>(), stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    ///     Stops the host and its <see cref="IHostedService" /> services.
    /// </summary>
    /// <returns>A task that represents the asynchronous host shutdown operation.</returns>
    public static async Task StopAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
    }

    /// <summary>
    ///     Gets the service of the specified type from the add-in service provider.
    /// </summary>
    /// <typeparam name="T">The type of service to resolve.</typeparam>
    /// <returns>The service instance.</returns>
    /// <exception cref="System.InvalidOperationException">No service of type <typeparamref name="T" /> is registered.</exception>
    /// <remarks>
    ///     Only for ribbon buttons: Revit creates <c>ExternalCommand</c> classes itself, so they cannot take
    ///     constructor dependencies. Everything else uses constructor injection.
    /// </remarks>
    public static T GetService<T>() where T : class
    {
        return _host!.Services.GetRequiredService<T>();
    }

    [LoggerMessage(LogLevel.Information, "Host started in {ElapsedMilliseconds} ms")]
    private static partial void LogHostStarted(ILogger logger, long elapsedMilliseconds);
}
