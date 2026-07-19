using AnalyseTool.App.Common.Bootstrap;
using AnalyseTool.App.Common.Extensions;
using AnalyseTool.App.Common.Transport;
using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using System.Text;
using System.Windows.Controls;

namespace AnalyseTool.App.Common.Docking
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
        private WebView2 _webView = new(); // replaced wholesale on recovery (see RebuildWebView)
        private readonly HashSet<string> _mappedHosts = new(StringComparer.OrdinalIgnoreCase);
        private WebView2Transport? _transport;
        private bool _initStarted;
        private bool _docEventsWired;

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
            _navigate = () => _webView.CoreWebView2.Navigate(ClientAppHost.ResolveUrl(_webView.CoreWebView2, hash));
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


                _webView.CoreWebView2.ContextMenuRequested += (s, args) =>
                {
                    args.Handled = true;
                };

                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.CoreWebView2.Settings.IsPinchZoomEnabled = false;

                // A crashed renderer is the classic "pane suddenly turns black" — recover in place.
                // A dead browser process can't Reload; tear down so the retry path rebuilds everything.
                _webView.CoreWebView2.ProcessFailed += OnWebViewProcessFailed;
                _webView.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess)
                        Log.Warning("Dock pane navigation failed: {Status}", e.WebErrorStatus);
                };

                // window.AT for extension pages (the clientapp overrides with its own equivalent).
                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionBridgeScript.Js);

                _transport = new WebView2Transport(_webView, AnalyseToolBootstrap.Dispatcher);
                _transport.Attach();

                // The pane outlives any document, so push document switches to the page — it re-queries
                // its data (the document itself is never cached host-side). Wire once: the pane is a
                // session-long singleton, and the tracker raises on the same UI thread the WebView lives on.
                if (!_docEventsWired)
                {
                    _docEventsWired = true;
                    DocumentTracker.DocumentChanged += title =>
                        _transport?.SendEvent("DocumentChanged", new { title });
                }

                _navigate?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize the AnalyseTool dockable pane");
                _initStarted = false; // allow a later ShowRoute/ShowExtension/Loaded to retry
                ShowInitError(ex.Message);
            }
        }

        private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Log.Warning("Dock pane WebView2 process failed: {Kind}", e.ProcessFailedKind);
            try
            {
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                    ShowInitError("The embedded browser process exited.");
                else
                    _webView.Reload(); // renderer/GPU process crash — the browser itself is still alive
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Dock pane WebView2 recovery failed");
                ShowInitError(ex.Message);
            }
        }

        /// <summary>Replaces the (possibly dead) WebView with a fresh control and reruns the full init.
        /// A wholesale rebuild is the one path that is safe for EVERY failure mode: after a browser
        /// process exit the old control is unusable, and rerunning init on a live control would attach a
        /// second transport and inject the bridge script twice.</summary>
        private void RebuildWebView()
        {
            try { _transport?.Detach(); } catch { /* dead WebView — nothing to detach from */ }
            _transport = null;
            _mappedHosts.Clear();
            try { _webView.Dispose(); } catch { /* already torn down */ }

            _webView = new WebView2();
            _initStarted = false;
            Content = _webView;
            _ = InitializeAsync();
        }

        /// <summary>A visible failure state instead of a silently black pane, with a Retry button.</summary>
        private void ShowInitError(string message)
        {
            var retry = new System.Windows.Controls.Button
            {
                Content = "Retry",
                Padding = new System.Windows.Thickness(16, 4, 16, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            retry.Click += (_, _) => RebuildWebView();
            Content = new StackPanel
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = "AnalyseTool panel failed to load.",
                        FontWeight = System.Windows.FontWeights.SemiBold,
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        Margin = new System.Windows.Thickness(0, 0, 0, 4),
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        Opacity = 0.7,
                        Margin = new System.Windows.Thickness(0, 0, 0, 12),
                    },
                    retry,
                },
            };
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
