using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Commands.Ai
{
    internal class AiAnalyse : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            AnalyzeParameterWithAiRequest? request = payload.ToObject<AnalyzeParameterWithAiRequest>();
            if (request == null) return;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Model);
                string raw = await ai.AnalyzeAsync(request.Items, request.Prompt);

                string json = JsonUtils.BuildResponce(nameof(AiAnalyse), raw);

                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (OperationCanceledException)
            {
                WebViewErrorHelper.SendError(webView, nameof(AiAnalyse), "AI Timeout: The model did not respond in time.");
            }
            catch (UnauthorizedAccessException ex)
            {
                WebViewErrorHelper.SendError(webView, nameof(AiAnalyse), ex.Message);
            }
            catch (Exception ex)
            {
                WebViewErrorHelper.SendError(webView, nameof(AiAnalyse), ex.Message);
            }
        }
    }
}
