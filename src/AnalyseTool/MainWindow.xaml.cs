using AnalyseTool.Common;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Interop;


namespace AnalyseTool
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

            string fileUrl = string.Empty;
#if RELEASE_R25 || RELEASE_R26
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app", PathProvider.RootDirectory, CoreWebView2HostResourceAccessKind.Allow);
            fileUrl = "https://app/index.html";
            webView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                webView.CoreWebView2.Navigate(fileUrl);
            };
#else
            webView.CoreWebView2.OpenDevToolsWindow();
            fileUrl = PathProvider.DebugServerUrl;
#endif
            webView.Source = new Uri(fileUrl);
        }
    }
}
