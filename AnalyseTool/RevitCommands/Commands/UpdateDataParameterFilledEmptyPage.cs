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
            UpdateDataParameterFilledEmptyPagePayload? data = payload.ToObject<UpdateDataParameterFilledEmptyPagePayload>();
            if (string.IsNullOrEmpty(data.CategoryName)) return;

            IEnumerable<DataElement> dataModels = DataElementsCollectorUtils.GetAllElementsByCategory(Context.Document, data.CategoryName)?.ToList() ?? new List<DataElement>();
            
            string json = JsonConvert.SerializeObject(new WebViewMessage() 
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(CommandsEnum.updateDataParameterFilledEmptyPage),
                Payload = JArray.FromObject(dataModels)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private record UpdateDataParameterFilledEmptyPagePayload
        {
            public string CategoryName { get; set; }
        }
    }
}