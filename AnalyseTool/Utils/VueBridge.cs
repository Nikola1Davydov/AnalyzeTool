using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.Utils
{
    public static class VueBridge
    {
        private static CoreWebView2Environment? _env; // глобальный env

        public static WebView2 WebView { get; set; }

        public static void SendToWebView(string json)
        {
            if (WebView?.CoreWebView2 != null)
            {
                WebView.CoreWebView2.PostWebMessageAsString(json);
            }
        }

        public static async Task InitWebView(WebView2 webView, string distFolder)
        {
            WebView = webView;
            if (_env == null)
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevitPluginWebView2");

                _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }

            if (WebView.CoreWebView2 == null)
            {
                await WebView.EnsureCoreWebView2Async(_env);

                WebView.CoreWebView2.OpenDevToolsWindow();

                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app", distFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                //webView.Source = new Uri("https://app/index.html");
                WebView.Source = new Uri("http://localhost:5173"); ;
            }
        }

    }
}
