using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetDocumentHealthStatus : IRevitTask
    {
        public void Execute(JToken payload, WebView2 webView)
        {
            int allWarningsInDocument = Context.Document.GetWarnings().Count;

            DocumentHealth docHealth = new DocumentHealth()
            {
                TotalWarnings = data("Total Warnings", allWarningsInDocument),
            };

            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(CommandsEnum.GetDocumentHealth),
                Payload = JArray.FromObject(docHealth)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private KeyValuePair<string, int> data(string titel, int count) =>
            new KeyValuePair<string, int>(titel, count);
    }
}
