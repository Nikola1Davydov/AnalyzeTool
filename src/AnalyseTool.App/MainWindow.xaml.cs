using AnalyseTool.App.Common;
using AnalyseTool.Core.Common;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Interop;


namespace AnalyseTool.App
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;

            nint revitHandle = Context.UiApplication.MainWindowHandle;
            WindowInteropHelper helper = new(this)
            {
                Owner = revitHandle
            };
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen", Justification = "<Ausstehend>")]
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.ContextMenuRequested += (s, args) =>
            {
                args.Handled = true;
            };

            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            webView.CoreWebView2.Settings.IsPinchZoomEnabled = false;

            string fileUrl = ClientAppHost.ResolveUrl(webView.CoreWebView2);
#if !ATRELEASE
            webView.CoreWebView2.OpenDevToolsWindow();
#endif
            webView.Source = new Uri(fileUrl);
        }
    }
}
