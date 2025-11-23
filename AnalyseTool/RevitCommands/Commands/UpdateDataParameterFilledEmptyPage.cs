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
        public void Execute(JToken payload, WebView2 webView)
        {
            string? categoryName = payload["categoryName"]?.ToString();
            if (string.IsNullOrEmpty(categoryName)) return;

            IEnumerable<DataElement> dataModels = DataElementsCollectorUtils.GetAllElementsByCategory(Context.Document, categoryName)?.ToList() ?? new List<DataElement>();
            
            string json = JsonConvert.SerializeObject(new WebViewMessage() 
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(CommandsEnum.updateDataParameterFilledEmptyPage),
                Payload = JArray.FromObject(dataModels)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}