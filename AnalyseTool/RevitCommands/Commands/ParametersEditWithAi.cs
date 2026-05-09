using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.Services;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class ParametersEditWithAi : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            AnalyzeParameterWithAiRequest? list = payload.ToObject<AnalyzeParameterWithAiRequest>();
            if (list == null) return;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(list.Model);
                AiAnalysisService.AiResponce result = await ai.AnalyzeAndEditAsync(list.Items, list.Prompt);

                var resultPayload = new JObject
                {
                    ["edits"] = JArray.FromObject(result.Edits),
                    ["raw"] = result.Raw,
                    ["error"] = JValue.CreateNull()
                };

                string json = JsonConvert.SerializeObject(new WebViewMessage()
                {
                    Type = WebMessageTypeEnum.Response.ToString(),
                    Command = nameof(ParametersEditWithAi),
                    Payload = resultPayload
                });

                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (OperationCanceledException)
            {
                WebViewErrorHelper.SendError(webView, nameof(ParametersEditWithAi), "KI-Zeitüberschreitung: Das Modell hat nicht rechtzeitig geantwortet.");
            }
            catch (Exception ex)
            {
                WebViewErrorHelper.SendError(webView, nameof(ParametersEditWithAi), ex.Message);
            }
        }
    }
}
