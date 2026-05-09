using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.Services;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class AnalyseWithAi : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            AnalyzeParameterWithAiRequest? request = payload.ToObject<AnalyzeParameterWithAiRequest>();
            if (request == null) return;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Model);
                string raw = await ai.AnalyzeAsync(request.Items, request.Prompt);

                string json = JsonConvert.SerializeObject(new WebViewMessage()
                {
                    Type = WebMessageTypeEnum.Response.ToString(),
                    Command = nameof(AnalyseWithAi),
                    Payload = new JValue(raw)
                });

                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (OperationCanceledException)
            {
                WebViewErrorHelper.SendError(webView, nameof(AnalyseWithAi), "KI-Zeitüberschreitung: Das Modell hat nicht rechtzeitig geantwortet.");
            }
            catch (Exception ex)
            {
                WebViewErrorHelper.SendError(webView, nameof(AnalyseWithAi), ex.Message);
            }
        }
    }
}
