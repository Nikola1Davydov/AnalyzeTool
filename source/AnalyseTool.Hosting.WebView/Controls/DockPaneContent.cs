using System.Windows.Controls;
using Autodesk.Revit.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.Hosting.WebView.Controls;

/// <summary>
///     The content of the one dock pane of the add-in.
/// </summary>
/// <remarks>
///     <para>
///         Revit allows registering a pane only in <c>OnStartup</c>, before the host is built, so the content starts as
///         an empty control and gets its <see cref="WebViewHost" /> when the host starts. Revit may restore the pane from a
///         saved layout at any time after that; the WebView initializes on <c>Loaded</c>.
///     </para>
///     <para>One pane for every page: another route only navigates the same WebView.</para>
/// </remarks>
internal sealed class DockPaneContent : ContentControl
{
    private WebViewServices? _services;
    private string _route;

    public DockPaneContent(DockablePaneId id, string route)
    {
        Id = id;
        _route = route;
        Loaded += (_, _) => EnsureHost();
    }

    public DockablePaneId Id { get; }

    /// <summary>Called when the host starts: from then on the pane can show its page.</summary>
    public void Attach(WebViewServices services)
    {
        _services = services;
        Dispatcher.InvokeAsync(EnsureHost);
    }

    public void Navigate(string route)
    {
        _route = route;
        if (Content is WebViewHost host)
        {
            host.Navigate(route);
        }
        else
        {
            EnsureHost();
        }
    }

    private void EnsureHost()
    {
        if (_services is not null && Content is not WebViewHost && IsLoaded)
        {
            Content = new WebViewHost(_services, _route);
        }
    }
}

/// <summary>Hands the services to the dock pane when the host starts.</summary>
internal sealed class DockPaneAttachment(DockPaneContent pane, IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        pane.Attach(services.GetRequiredService<WebViewServices>());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
