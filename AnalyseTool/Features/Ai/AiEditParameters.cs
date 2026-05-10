using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Ai
{
    internal class AiEditParameters : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            AnalyzeParameterWithAiRequest? list = payload.ToObject<AnalyzeParameterWithAiRequest>();
            if (list == null) return;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(list.Model);
                AiAnalysisService.AiResponce result = await ai.AnalyzeAndEditAsync(list.Items, list.Prompt);

                JObject resultPayload = new JObject
                {
                    ["edits"] = JArray.FromObject(result.Edits),
                    ["raw"] = result.Raw,
                    ["error"] = JValue.CreateNull()
                };

                string json = JsonUtils.BuildResponce(nameof(AiEditParameters), resultPayload);

                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (OperationCanceledException)
            {
                WebViewErrorHelper.SendError(webView, nameof(AiEditParameters), "KI-Zeitüberschreitung: Das Modell hat nicht rechtzeitig geantwortet.");
            }
            catch (UnauthorizedAccessException ex)
            {
                WebViewErrorHelper.SendError(webView, nameof(AiAnalyse), ex.Message);
            }
            catch (Exception ex)
            {
                WebViewErrorHelper.SendError(webView, nameof(AiEditParameters), ex.Message);
            }
        }
    }
}
