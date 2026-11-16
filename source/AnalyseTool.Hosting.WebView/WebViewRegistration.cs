using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.WebView.ClientApp;
using AnalyseTool.Hosting.WebView.Controls;
using AnalyseTool.Hosting.WebView.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Nice3point.Revit.Toolkit.Decorators;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the AnalyseTool.Hosting.WebView module.
/// </summary>
[PublicAPI]
public static class WebViewRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds <see cref="IWindows" />, the shared WebView2 environment and, optionally, the dock pane.
        /// </summary>
        /// <param name="application">The application Revit passed to the add-in at startup.</param>
        /// <param name="configure">The dock pane, if the add-in has one.</param>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>Call inside <c>OnStartup</c>, before the first <c>await</c>: Revit registers dock panes only there.</remarks>
        /// <exception cref="InvalidOperationException"><c>AddRevitCommands</c> was not called before.</exception>
        /// <example>
        ///     <code>
        ///     builder.AddWebView(application, webView => webView
        ///         .DockPane(new Guid("…"), "My add-in", "/dock"));
        ///     </code>
        /// </example>
        public TBuilder AddWebView(UIControlledApplication application, Action<WebViewBuilder>? configure = null)
        {
            if (builder.Services.All(service => service.ServiceType != typeof(ICommandExecutor)))
            {
                throw new InvalidOperationException("Call AddRevitCommands before AddWebView: the pages call the command pipeline.");
            }

            var options = new WebViewBuilder();
            configure?.Invoke(options);

            builder.Services.Configure<ClientAppOptions>(builder.Configuration.GetSection(ClientAppOptions.SectionName));
            builder.Services.AddSingleton<WebViewEnvironmentProvider>();
            builder.Services.AddSingleton<ClientAppSource>();
            builder.Services.AddSingleton(new WebViewSecurity(options.NavigationRestricted));
            builder.Services.AddSingleton<WebViewServices>();

            if (options.Pane is { } pane)
            {
                var content = new DockPaneContent(new DockablePaneId(pane.Id), pane.Route);
                DockablePaneProvider
                    .Register(application, pane.Id, pane.Title)
                    .SetConfiguration(data =>
                    {
                        data.FrameworkElement = content;
                        data.InitialState = new DockablePaneState
                        {
                            DockPosition = DockPosition.Tabbed,
                            TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
                        };
                    });

                builder.Services.AddSingleton(content);
                builder.Services.AddHostedService<DockPaneAttachment>();
            }

            builder.Services.AddSingleton<IWindows, WindowManager>();
            return builder;
        }
    }
}

/// <summary>
///     Configures <c>AddWebView</c>.
/// </summary>
[PublicAPI]
public sealed class WebViewBuilder
{
    internal (Guid Id, string Title, string Route)? Pane { get; private set; }

    internal bool NavigationRestricted { get; private set; } = true;

    /// <summary>
    ///     Registers the one dock pane of the add-in. Every page shows in it by route (<see cref="IWindows.ShowDockPane" />).
    /// </summary>
    /// <param name="id">A stable id: Revit remembers where the user docked the pane by it. Unique per add-in.</param>
    /// <param name="title">The pane caption.</param>
    /// <param name="route">The route shown first, also when Revit restores the pane at startup.</param>
    public WebViewBuilder DockPane(Guid id, string title, string route)
    {
        Pane = (id, title, route);
        return this;
    }

    /// <summary>
    ///     Turns off the navigation protection, which is on by default: any page may then load in the
    ///     WebView and send commands.
    /// </summary>
    /// <remarks>
    ///     Any page in the WebView can call commands, so by default only the add-in's own pages load and talk to the
    ///     pipeline: a navigation to another address is cancelled and <c>http(s)</c> links — also <c>target="_blank"</c> —
    ///     open in the system browser; messages from another origin are dropped. Turn it off only when the add-in has to
    ///     show another site inside its own window and that site is trusted.
    /// </remarks>
    public WebViewBuilder AllowAnyNavigation()
    {
        NavigationRestricted = false;
        return this;
    }
}
