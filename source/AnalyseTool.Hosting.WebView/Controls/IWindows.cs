using System.Windows;
using Autodesk.Revit.UI;
using Microsoft.Extensions.Hosting;
using Nice3point.Revit.Toolkit;

namespace AnalyseTool.Hosting.WebView.Controls;

/// <summary>
///     Opens the pages of the add-in: windows and the dock pane. Call from the Revit UI thread — a
///     ribbon button.
/// </summary>
[PublicAPI]
public interface IWindows
{
    /// <summary>
    ///     Opens a <see cref="WebViewWindow" /> with the route; a second call brings the open window of that route to front.
    /// </summary>
    /// <param name="route">The page route, for example <c>/families</c>.</param>
    /// <param name="configure">Title and size of a new window.</param>
    void Open(string route, Action<WebViewWindowOptions>? configure = null);

    /// <summary>Shows the dock pane with the route.</summary>
    /// <exception cref="InvalidOperationException">No dock pane was registered with <c>AddWebView(…, w =&gt; w.DockPane(…))</c>.</exception>
    void ShowDockPane(string route);
}

/// <summary>One window per route: showing an open route brings its window to the front instead of opening a second one.</summary>
internal sealed class WindowManager(
    WebViewServices services,
    UIControlledApplication application,
    IHostEnvironment environment,
    DockPaneContent? dockPane = null) : IWindows
{
    private readonly Dictionary<string, WebViewWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Open(string route, Action<WebViewWindowOptions>? configure = null)
    {
        if (_windows.TryGetValue(route, out var open))
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var options = new WebViewWindowOptions();
        configure?.Invoke(options);

        var window = new WebViewWindow(new WebViewHost(services, route), options, environment.ApplicationName, application.MainWindowHandle);
        window.Closed += (_, _) => _windows.Remove(route);
        _windows[route] = window;
        window.Show();
    }

    /// <inheritdoc />
    public void ShowDockPane(string route)
    {
        if (dockPane is null)
        {
            throw new InvalidOperationException("No dock pane is registered: add it with builder.AddWebView(application, w => w.DockPane(id, title, route)).");
        }

        dockPane.Navigate(route);
        // UIApplication, not the startup UIControlledApplication: a button runs long after OnStartup
        RevitContext.UiApplication.GetDockablePane(dockPane.Id).Show();
    }
}
