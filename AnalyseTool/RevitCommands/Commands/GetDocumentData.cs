using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class GetDocumentData : IRevitTask
    {
        public void Execute(JToken payload, WebView2 webView)
        {
            Document doc = Context.Document;
            DocumentData documentData = new DocumentData()
            {
                Name = doc.Title,
                Id = doc.CreationGUID.ToString()
            };
            string json = JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = nameof(GetDocumentData),
                Payload = JObject.FromObject(documentData)
            });

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }

    }
}
