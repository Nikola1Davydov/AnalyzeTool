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
            AnalyzeWithAiDto? list = payload.ToObject<AnalyzeWithAiDto>();

            AiAnalysisService ai = new AiAnalysisService();
            List<AiAnalysisService.ParameterEdit> edits = await ai.AnalyzeAsync(list.Items, list.Prompt);

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(AnalyzeWithAi),
                Payload = JObject.FromObject(edits)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private sealed record AnalyzeWithAiDto()
        {
            [JsonProperty("items")]
            public List<ParameterData> Items { get; set; } = new();

            [JsonProperty("prompt")]
            public string Prompt { get; set; }
        }
    }
}
