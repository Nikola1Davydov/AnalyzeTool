using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Common.Transport;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using System.Text;
using System.Windows.Controls;

namespace AnalyseTool.Common.Docking
{
    /// <summary>
    /// The single WebView2 surface hosted inside Revit's one AnalyseTool dockable pane. Its content is a
    /// navigation, not a new pane: a built-in screen is a clientapp hash route (e.g. <c>#/families-dock</c>),
    /// and an extension is its own page served over a private virtual host — the same WebView hosts both,
    /// so features AND extensions share the dock without per-extension pane registration.
    ///
    /// Init is deferred to <see cref="FrameworkElement.Loaded"/> because Revit may re-create this pane on
    /// startup (restoring a docked layout) BEFORE any ribbon click initialized the host dispatcher. We
    /// await <see cref="DockPaneHost.EnsureReadyAsync"/> before wiring the transport, so it can safely
    /// come up on its own. The <c>window.AT</c> bridge is injected (as for extension windows) so extension
    /// pages can call host commands; the clientapp installs its own equivalent and is unaffected.
    /// </summary>
    internal sealed class AnalyseToolDockPane : UserControl
    {
        private readonly WebView2 _webView = new();
        private readonly HashSet<string> _mappedHosts = new(StringComparer.OrdinalIgnoreCase);
        private WebView2Transport? _transport;
        private bool _initStarted;

        // Deferred navigation: set by ShowRoute/ShowExtension, applied once the WebView is ready (or
        // immediately if it already is). Defaults to the family placement palette.
        private Action? _navigate;

        public AnalyseToolDockPane()
        {
            Content = _webView;
            ShowRoute("#/families-dock");
            Loaded += (_, _) => _ = InitializeAsync();
        }

        /// <summary>Points the pane at a built-in clientapp hash route (e.g. "#/families-dock").</summary>
        public void ShowRoute(string route)
        {
            string hash = route.StartsWith("#") ? route : "#" + route;
            _navigate = () =>
            {
                string url;
#if RELEASE_R25 || RELEASE_R26
                EnsureHostMapping("app", PathProvider.RootDirectory);
                url = "https://app/index.html" + hash;
#else
                url = PathProvider.DebugServerUrl.TrimEnd('/') + "/" + hash;
#endif
                _webView.CoreWebView2.Navigate(url);
            };
            if (_webView.CoreWebView2 is not null) _navigate();
        }

        /// <summary>Points the pane at an extension's page — its dev server when set, otherwise its folder
        /// served over a private virtual host (same scheme as the standalone extension window).</summary>
        public void ShowExtension(string id, string directory, string? devUrl, string entryHtml)
        {
            _navigate = () =>
            {
                if (!string.IsNullOrWhiteSpace(devUrl))
                {
                    _webView.CoreWebView2.Navigate(devUrl);
                    return;
                }

                string host = BuildHostName(id);
                EnsureHostMapping(host, directory);
                string entry = (entryHtml ?? "index.html").Replace('\\', '/').TrimStart('/');
                _webView.CoreWebView2.Navigate($"https://{host}/{entry}");
            };
            if (_webView.CoreWebView2 is not null) _navigate();
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

                // window.AT for extension pages (the clientapp overrides with its own equivalent).
                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionBridgeScript.Js);

                _transport = new WebView2Transport(_webView, AnalyseToolBootstrap.Dispatcher);
                _transport.Attach();

                _navigate?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize the AnalyseTool dockable pane");
                _initStarted = false; // allow a later ShowRoute/ShowExtension/Loaded to retry
            }
        }

        /// <summary>Maps a virtual host to a folder once — remapping the same host would throw.</summary>
        private void EnsureHostMapping(string host, string folder)
        {
            if (!_mappedHosts.Add(host)) return;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                host, folder, CoreWebView2HostResourceAccessKind.Allow);
        }

        private static string BuildHostName(string id)
        {
            StringBuilder sb = new("ext-");
            foreach (char c in id.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '-');
            return sb.ToString();
        }
    }
}
