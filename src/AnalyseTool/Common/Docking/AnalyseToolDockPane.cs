using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Transport;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using System.Windows.Controls;

namespace AnalyseTool.Common.Docking
{
    /// <summary>
    /// The single WebView2 surface hosted inside Revit's one AnalyseTool dockable pane. It loads the
    /// same clientapp as the windows (hash routes), so a docked screen is just a route (e.g.
    /// <c>#/families-dock</c>) — no second UI stack. Switching what the pane shows is a navigation, not a
    /// new pane (see <see cref="DockPaneHost"/>).
    ///
    /// Init is deferred to <see cref="FrameworkElement.Loaded"/> because Revit may re-create this pane on
    /// startup (restoring a previously docked layout) BEFORE any ribbon click has initialized the host
    /// dispatcher. We therefore await <see cref="DockPaneHost.EnsureReadyAsync"/> before wiring the
    /// transport, so the pane can safely come up on its own.
    /// </summary>
    internal sealed class AnalyseToolDockPane : UserControl
    {
        private readonly WebView2 _webView = new();
        private WebView2Transport? _transport;
        private bool _initStarted;

        // Route to show once the WebView is ready. Defaults to the family placement palette; a later
        // ShowRoute (from another ribbon button / an extension) overrides it, navigating live if ready.
        private string _route = "#/families-dock";

        public AnalyseToolDockPane()
        {
            Content = _webView;
            Loaded += (_, _) => _ = InitializeAsync();
        }

        /// <summary>Points the pane at a hash route and navigates to it if the WebView is already up;
        /// otherwise the route is applied when initialization finishes.</summary>
        public void ShowRoute(string route)
        {
            _route = route.StartsWith("#") ? route : "#" + route;
            if (_webView.CoreWebView2 is not null) Navigate();
        }

        private async Task InitializeAsync()
        {
            if (_initStarted) return;
            _initStarted = true;

            try
            {
                // The pane may be restored at startup before any command initialized the host — wait
                // until the dispatcher exists (via a one-shot Idling that yields a live UIApplication).
                await DockPaneHost.EnsureReadyAsync();

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath);
                await _webView.EnsureCoreWebView2Async(env);

                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.CoreWebView2.Settings.IsPinchZoomEnabled = false;

                _transport = new WebView2Transport(_webView, AnalyseToolBootstrap.Dispatcher);
                _transport.Attach();

                Navigate();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize the AnalyseTool dockable pane");
                _initStarted = false; // allow a later ShowRoute/Loaded to retry
            }
        }

        private void Navigate()
        {
            string url;
#if RELEASE_R25 || RELEASE_R26
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app", PathProvider.RootDirectory, CoreWebView2HostResourceAccessKind.Allow);
            url = "https://app/index.html" + _route;
#else
            url = PathProvider.DebugServerUrl.TrimEnd('/') + "/" + _route;
#endif
            _webView.CoreWebView2.Navigate(url);
        }
    }
}
