using AnalyseTool.RevitCommands.DataModel;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetCategoriesInRevit : IRevitTask
    {
        public void Execute(JToken data, WebView2 webView)
        {
            List<string> allCategories = DataElementsCollectorUtils.GetModelCategoriesNames(Context.Document);

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(CommandsEnum.GetCategories),
                Payload = JArray.FromObject(allCategories)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
