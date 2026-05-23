using AnalyseTool.Common;
using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Transport;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>
    /// Hosts one JS extension page in its own top-level WebView2 window: maps the extension folder
    /// to a private virtual host, injects the window.AT bridge, attaches a transport to the shared
    /// command dispatcher, and navigates to the extension's entry HTML.
    /// </summary>
    internal sealed class ExtensionWindow : Window
    {
        private readonly ExtensionDescriptor _extension;
        private readonly WebView2 _webView = new();
        private WebView2Transport? _transport;

        public ExtensionWindow(ExtensionDescriptor extension)
        {
            _extension = extension;
            Title = string.IsNullOrWhiteSpace(extension.Manifest.DisplayName)
                ? extension.Manifest.Id
                : extension.Manifest.DisplayName;
            Width = 1100;
            Height = 750;
            Content = _webView;

            _ = new WindowInteropHelper(this) { Owner = Context.UiApplication.MainWindowHandle };
            Loaded += OnLoaded;
            Closed += (_, _) => _transport?.Detach();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath);
            await _webView.EnsureCoreWebView2Async(env);

            // Inject the bridge first so window.AT exists before the page's own scripts run
            // (works the same on a dev-server origin or the virtual host).
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionBridgeScript.Js);

            _transport = new WebView2Transport(_webView, AnalyseToolBootstrap.Dispatcher);
            _transport.Attach();

            string? devUrl = _extension.Manifest.Ui?.DevUrl;
            if (!string.IsNullOrWhiteSpace(devUrl))
            {
                // Development: load the live dev server (Vite/HMR). Author removes devUrl for release.
                _webView.CoreWebView2.Navigate(devUrl);
                return;
            }

            // Production: serve the extension folder over a private virtual host.
            string host = BuildHostName(_extension.Manifest.Id);
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                host, _extension.Directory, CoreWebView2HostResourceAccessKind.Allow);

            // Accept sub-paths ("Acme/index.html"); tolerate backslashes and a leading slash.
            string entryHtml = (_extension.Manifest.Ui?.EntryHtml ?? "index.html")
                .Replace('\\', '/')
                .TrimStart('/');
            _webView.CoreWebView2.Navigate($"https://{host}/{entryHtml}");
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
