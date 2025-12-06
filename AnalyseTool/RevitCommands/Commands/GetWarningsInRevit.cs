using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetWarningsInRevit : IRevitTask
    {
        public void Execute(JToken payload, WebView2 webView)
        {
            List<WarningInRevitModel> warningInRevitModels = new List<WarningInRevitModel>();
            IList<FailureMessage> allWarningsInDocument = Context.Document.GetWarnings();
            foreach (var item in allWarningsInDocument)
            {
                var warningModel = new WarningInRevitModel()
                {
                    AdditionalElements = item.GetAdditionalElements().Select(x => x.Value).ToList(),
                    FailingElements = item.GetFailingElements().Select(x => x.Value).ToList(),
                    WarningDescription = item.GetDescriptionText(),
                };
                warningInRevitModels.Add(warningModel);
            }
            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(CommandsEnum.GetWarnings),
                Payload = JArray.FromObject(warningInRevitModels)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
