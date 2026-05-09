using AnalyseTool.Common.Model;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Common.FeaturesBase
{
    internal static class WebViewErrorHelper
    {
        public static void SendError(WebView2 webView, string command, string message)
        {
            string json = JsonConvert.SerializeObject(new WebViewMessage
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Payload = JObject.FromObject(new { error = message })
            });
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
