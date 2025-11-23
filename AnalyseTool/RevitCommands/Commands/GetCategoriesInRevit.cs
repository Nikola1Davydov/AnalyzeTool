using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.DataModel;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetCategoriesInRevit : IRevitTask
    {
        public void Execute(object data, WebView2 webView)
        {
            List<string> allCategories = DataElementsCollectorUtils.GetModelCategoriesNames(Context.Document);

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                CommandsEnum = CommandsEnum.GetCategories,
                JsonData = allCategories
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
