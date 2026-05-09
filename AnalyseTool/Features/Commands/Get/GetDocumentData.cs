using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Common.Model;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Commands.Get
{
    internal class GetDocumentData : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            Document doc = Context.Document;
            DocumentData documentData = new DocumentData()
            {
                Name = doc.Title,
                Id = doc.CreationGUID.ToString()
            };

            string json = JsonUtils.BuildResponce(nameof(GetDocumentData), documentData);

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private record DocumentData()
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("id")]
            public string Id { get; set; }
        }
    }
}
