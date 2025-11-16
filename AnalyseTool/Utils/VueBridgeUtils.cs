using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace AnalyseTool.Utils
{
    public static class VueBridgeUtils
    {
        private static CoreWebView2Environment? _env; // глобальный env

        private static readonly string distFolder = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "dist");

        public static WebView2 WebView { get; set; }

        public static void SendToWebView(string json)
        {
            if (WebView?.CoreWebView2 != null)
            {
                WebView.CoreWebView2.PostWebMessageAsString(json);
            }
        }

        public static async Task InitWebView(WebView2 webView)
        {
            WebView = webView;
            if (_env == null)
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevitPluginWebView2");

                _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }

            if (WebView.CoreWebView2 == null)
            {
                await WebView.EnsureCoreWebView2Async(_env);

                WebView.CoreWebView2.OpenDevToolsWindow();

                //WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                //    "app", distFolder,
                //    CoreWebView2HostResourceAccessKind.Allow);

                webView.CoreWebView2.WebMessageReceived += (sender, args) =>
                {
                    string json = args.WebMessageAsJson;
                    // десериализуем в объект
                    //var data = JsonSerializer.Deserialize<VueCommands>(json);

                    //// теперь можешь работать с объектом
                    //Debug.WriteLine($"Got category: {data.Category}");
                };

                //webView.Source = new Uri("https://app/index.html");
                WebView.Source = new Uri("http://localhost:22524");
            }
        }

    }
}
