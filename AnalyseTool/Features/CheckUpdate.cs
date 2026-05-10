using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Common.Helper.Updater;
using AnalyseTool.Common.Model;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features
{
    internal class CheckUpdate : IRevitTask
    {
        public UpdateInfo? UpdateInfo { get; private set; }
        public async Task Execute(JToken payload, WebView2 webView)
        {
            await GetUpdateData();

            string json = JsonUtils.BuildResponce(nameof(CheckUpdate), UpdateInfo);

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        public async Task GetUpdateData()
        {
            UpdateInfo = await GitHubUpdateChecker.CheckForUpdateAsync(owner: "Nikola1Davydov", repo: "AnalyzeTool");
        }
    }
}
