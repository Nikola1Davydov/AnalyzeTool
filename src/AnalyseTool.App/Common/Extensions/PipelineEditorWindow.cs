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
    /// The pipeline editor, in its own window (route <c>#/pipelines</c>). Same shape as
    /// <see cref="FamilyControlWindow"/>: the clientapp over a virtual host, with the command transport
    /// attached so <c>AT.invoke</c> reaches the dispatcher.
    ///
    /// <para>A window rather than the dockable pane, deliberately. The RUNNER is docked because it sits
    /// beside the model reporting node by node; a canvas needs room and is unusable in a pane tabbed
    /// behind the Project Browser. The two share nothing but the command catalogue.</para>
    ///
    /// <para>Opened by name (<c>?name=</c>) when the runner asks to edit the selected pipeline, and with
    /// no name for a blank one. The route reads it from the hash query rather than from a host call, so
    /// the window needs no bridge of its own beyond the transport.</para>
    /// </summary>
    internal sealed class PipelineEditorWindow : Window
    {
        private readonly WebView2 _webView = new();
        private readonly string _route;
        private WebView2Transport? _transport;

        public PipelineEditorWindow(string? pipelineName = null)
        {
            _route = string.IsNullOrWhiteSpace(pipelineName)
                ? "#/pipelines"
                : "#/pipelines?name=" + Uri.EscapeDataString(pipelineName);

            Title = "AnalyseTool — Pipeline Editor";
            Width = 1280;
            Height = 800;
            MinWidth = 640;
            MinHeight = 400;
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

            _webView.CoreWebView2.Navigate(ClientAppHost.ResolveUrl(_webView.CoreWebView2, _route));
        }
    }
}
