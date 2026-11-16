using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.Hosting.WebView.Controls;

/// <summary>
///     The options of a <see cref="WebViewWindow" />, as <c>IWindows.Open(route, o =&gt; …)</c> sets them.
/// </summary>
[PublicAPI]
public sealed class WebViewWindowOptions
{
    /// <summary>Gets or sets the title; by default the add-in name.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the initial width.</summary>
    public double Width { get; set; } = 1000;

    /// <summary>Gets or sets the initial height.</summary>
    public double Height { get; set; } = 700;
}

/// <summary>
///     The one window class for every page: a page is a route of ClientApp, not a window class of its own.
/// </summary>
/// <remarks>Modeless and owned by the Revit main window, so it stays above Revit and closes with it.</remarks>
[PublicAPI]
public sealed class WebViewWindow : Window
{
    internal WebViewWindow(WebViewHost host, WebViewWindowOptions options, string defaultTitle, nint owner)
    {
        Host = host;
        Title = options.Title ?? defaultTitle;
        Width = options.Width;
        Height = options.Height;
        MinWidth = 320;
        MinHeight = 240;
        Content = host;

        _ = new WindowInteropHelper(this) { Owner = owner };
        Closed += (_, _) => host.Dispose();
    }

    /// <summary>Gets the WebView surface of the window.</summary>
    public WebViewHost Host { get; }
}
