using AnalyseTool.Common;
using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Transport;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.Features.Families
{
    /// <summary>
    /// "Family Control" page in its own window (the second ribbon button next to AnalyseTool). Loads the
    /// main clientapp at the hash route #/families (release: virtual host over the plugin folder; debug:
    /// dev server), reusing the app's Vue/PrimeVue UI and its built-in window.AT bridge. We only attach
    /// the command transport so AT.invoke reaches the dispatcher — same pattern as SettingsWindow.
    /// </summary>
    internal sealed class FamilyControlWindow : Window
    {
        private const string Route = "#/families";
        private readonly WebView2 _webView = new();
        private WebView2Transport? _transport;

        public FamilyControlWindow()
        {
            Title = "AnalyseTool — Family Control";
            Width = 1100;
            Height = 720;
            Content = _webView;

            _ = new WindowInteropHelper(this) { Owner = AnalyseTool.Context.UiApplication.MainWindowHandle };
            Loaded += OnLoaded;
            Closed += (_, _) => _transport?.Detach();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath);
            await _webView.EnsureCoreWebView2Async(env);

            _transport = new WebView2Transport(_webView, AnalyseToolBootstrap.Dispatcher);
            _transport.Attach();

            string url;
#if RELEASE_R25 || RELEASE_R26
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app", PathProvider.RootDirectory, CoreWebView2HostResourceAccessKind.Allow);
            url = "https://app/index.html" + Route;
#else
            url = PathProvider.DebugServerUrl.TrimEnd('/') + "/" + Route;
#endif
            _webView.CoreWebView2.Navigate(url);
        }
    }
}
