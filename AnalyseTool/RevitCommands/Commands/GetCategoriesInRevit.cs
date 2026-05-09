using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetCategoriesInRevit : IRevitTask
    {
        public async void Execute(JToken data, WebView2 webView)
        {
            List<string> allCategories = await Task.Run(() => DataElementsCollectorUtils.GetModelCategoriesNames(Context.Document));

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(GetCategoriesInRevit),
                Payload = JArray.FromObject(allCategories)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
