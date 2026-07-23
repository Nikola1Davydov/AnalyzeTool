using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.App.Common;
using AnalyseTool.App.Common.Bootstrap;
using AnalyseTool.App.Common.Transport;
using AnalyseTool.Core;
using AnalyseTool.Core.Common;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.App.Common.Extensions
{
    /// <summary>
    /// System "Extensions/Settings" page in its own window. Loads the main clientapp at the hash
    /// route #/system/settings (release: virtual host over the plugin folder; debug: dev server),
    /// reusing the app's Vue/PrimeVue UI and its built-in window.AT bridge. We only attach the
    /// command transport so AT.invoke reaches the dispatcher.
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private const string Route = "#/system/settings";
        private readonly WebView2 _webView = new();
        private WebView2Transport? _transport;

        public SettingsWindow()
        {
            Title = "AnalyseTool — Extensions";
            Width = 1000;
            MinHeight = 300;
            MinWidth = 300;
            Height = 680;
            Content = _webView;

            _ = new WindowInteropHelper(this) { Owner = Context.UiApplication.MainWindowHandle };
            Loaded += OnLoaded;
            Closed += (_, _) => _transport?.Detach();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.IsPinchZoomEnabled = false;

            _transport = new WebView2Transport(_webView, CoreServices.Queue);
            _transport.Attach();

            _webView.CoreWebView2.Navigate(ClientAppHost.ResolveUrl(_webView.CoreWebView2, Route));
        }
    }
}
