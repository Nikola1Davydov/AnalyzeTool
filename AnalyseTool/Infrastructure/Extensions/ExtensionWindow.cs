using AnalyseTool.Common;
using AnalyseTool.Infrastructure.Bootstrap;
using AnalyseTool.Infrastructure.Transport;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.Infrastructure.Extensions
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

            _ = new WindowInteropHelper(this) { Owner = AnalyseTool.Context.UiApplication.MainWindowHandle };
            Loaded += OnLoaded;
            Closed += (_, _) => _transport?.Detach();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath);
            await _webView.EnsureCoreWebView2Async(env);

            // Inject the bridge first so window.AT exists before the page's own scripts run.
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionBridgeScript.Js);

            string host = BuildHostName(_extension.Manifest.Id);
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                host, _extension.Directory, CoreWebView2HostResourceAccessKind.Allow);

            _transport = new WebView2Transport(_webView, AnalyseToolBootstrap.Dispatcher);
            _transport.Attach();

            string entryHtml = _extension.Manifest.Ui?.EntryHtml ?? "index.html";
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
