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
    /// A host window for one of the plugin's own system pages (Extensions, Settings, New extension).
    /// It loads the main clientapp at a hash route (release: virtual host over the plugin folder;
    /// debug: dev server), reusing the app's Vue/PrimeVue UI and its built-in window.AT bridge. We
    /// only attach the command transport so AT.invoke reaches the dispatcher.
    /// <para>One class, not one per page: the pages differ by route, title and size, nothing else.</para>
    /// </summary>
    internal sealed class SystemWindow : Window
    {
        private readonly WebView2 _webView = new();
        private WebView2Transport? _transport;
        private readonly string _route;

        public SystemWindow(string route, string title, double width, double height)
        {
            _route = route;
            Title = title;
            Width = width;
            Height = height;
            MinHeight = 300;
            MinWidth = 300;
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

            // A page that is a single form (New extension) closes itself with window.close() when it
            // is done or cancelled. WebView2 turns that into this event; without the handler the call
            // is silently ignored and the user is left with a finished form and a window to close by hand.
            _webView.CoreWebView2.WindowCloseRequested += (_, _) => Close();

            _webView.CoreWebView2.Navigate(ClientAppHost.ResolveUrl(_webView.CoreWebView2, _route));
        }
    }
}
