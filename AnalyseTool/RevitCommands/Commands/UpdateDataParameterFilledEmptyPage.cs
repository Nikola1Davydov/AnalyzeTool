using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.DataModel;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class UpdateDataParameterFilledEmptyPage : IRevitTask
    {
        public void Execute(object data, WebView2 webView)
        {
            string? categoryName = data.ToString();
            if (string.IsNullOrEmpty(categoryName)) return;

            IEnumerable<DataElement> dataModels = DataElementsCollectorUtils.GetAllElementsByCategory(Context.Document, categoryName)?.ToList() ?? new List<DataElement>();
            
            string json = JsonConvert.SerializeObject(new WebViewMessage() 
            {
                CommandsEnum = CommandsEnum.updateDataParameterFilledEmptyPage,
                JsonData = dataModels
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}