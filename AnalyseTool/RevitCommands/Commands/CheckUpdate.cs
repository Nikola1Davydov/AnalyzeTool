using AnalyseTool.Helper.Updater;
using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class CheckUpdate : IRevitTask
    {
        public UpdateInfo? UpdateInfo { get; private set; }
        public async Task Execute(JToken payload, WebView2 webView)
        {
            await GetUpdateData();

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(CheckUpdate),
                Payload = JObject.FromObject(UpdateInfo)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        public async Task GetUpdateData()
        {
            UpdateInfo = await GitHubUpdateChecker.CheckForUpdateAsync(owner: "Nikola1Davydov", repo: "AnalyzeTool");
        }
    }
}
