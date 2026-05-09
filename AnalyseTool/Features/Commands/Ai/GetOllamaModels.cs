using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Common.Model;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace AnalyseTool.Features.Commands.Ai
{
    internal class GetOllamaModels : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            List<string> models = new();
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
                string json = await http.GetStringAsync("http://localhost:11434/api/tags");

                JObject obj = JObject.Parse(json);
                models = obj["models"]!
                    .Select(m => m["name"]!.Value<string>()!)
                    .ToList();
            }
            catch { /* Ollama is not available — returning an empty list */ }

            string response = JsonConvert.SerializeObject(new WebViewMessage
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(GetOllamaModels),
                Payload = JArray.FromObject(models)
            });

            webView.CoreWebView2.PostWebMessageAsJson(response);
        }
    }
}
