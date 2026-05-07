using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.Services;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class AnalyzeWithAi : IRevitTask
    {
        public async void Execute(JToken payload, WebView2 webView)
        {
            AnalyzeWithAiRequest? list = payload.ToObject<AnalyzeWithAiRequest>();
            if (list == null) return;

            try
            {
                AiAnalysisService ai = new AiAnalysisService();
                AiAnalysisService.AiResponce result = await ai.AnalyzeAsync(list.Items, list.Prompt);

                var resultPayload = new JObject
                {
                    ["edits"] = JArray.FromObject(result.Edits),
                    ["raw"] = result.Raw,
                    ["error"] = JValue.CreateNull()
                };

                string json = JsonConvert.SerializeObject(new WebViewMessage()
                {
                    Type = WebMessageTypeEnum.Response.ToString(),
                    Command = nameof(AnalyzeWithAi),
                    Payload = resultPayload
                });

                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                string errorJson = JsonConvert.SerializeObject(new WebViewMessage()
                {
                    Type = WebMessageTypeEnum.Response.ToString(),
                    Command = nameof(AnalyzeWithAi),
                    Payload = JObject.FromObject(new { error = ex.Message, raw = (string?)null })
                });

                webView.CoreWebView2.PostWebMessageAsJson(errorJson);
            }
        }
        private sealed record AnalyzeWithAiRequest()
        {
            [JsonProperty("items")]
            public List<ParameterData> Items { get; set; } = new();

            [JsonProperty("prompt")]
            public string Prompt { get; set; }
        }
    }
}
