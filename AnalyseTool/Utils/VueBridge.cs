using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Reflection;
using System.Windows;

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
            if (_env == null)
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevitPluginWebView2");

                _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }

            if (webView.CoreWebView2 == null)
            {
                await webView.EnsureCoreWebView2Async(_env);

                webView.CoreWebView2.OpenDevToolsWindow();

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app", distFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                // подписка на сообщения из Vue
                webView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    var msg = args.WebMessageAsJson;
                    System.Diagnostics.Debug.WriteLine("Vue → C#: " + msg);
                    // здесь разбираешь JSON и делаешь SelectElements(...)
                };

                webView.Source = new Uri("https://app/index.html");
            }
        }
    }
}
