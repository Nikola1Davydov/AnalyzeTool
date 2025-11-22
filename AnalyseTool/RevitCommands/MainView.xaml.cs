using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab
{
    /// <summary>
    /// Interaktionslogik für SubViewAnalyseTool.xaml
    /// </summary>
    public partial class MainView : Window
    {
        public MainView()
        {
            InitializeComponent();
            Loaded += Window_Loaded;

            IntPtr revitHandle = Context.UiApplication.MainWindowHandle;
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
#if RELEASE_R24 || RELEASE_R25 || RELEASE_R26
            //webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app", distFolder, CoreWebView2HostResourceAccessKind.Allow);
            fileUrl = PathProvider.ReleaseServerUrl;
#else
            webView.CoreWebView2.OpenDevToolsWindow();
            fileUrl = PathProvider.DebugServerUrl;
#endif
            webView.Source = new Uri(fileUrl);
        }
    }
}
