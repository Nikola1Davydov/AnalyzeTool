using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Common.Model;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Commands.Get
{
    internal class GetWarningsInRevit : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            List<WarningInRevitModel> warningInRevitModels = new List<WarningInRevitModel>();
            IList<FailureMessage> allWarningsInDocument = Context.Document.GetWarnings();
            foreach (FailureMessage item in allWarningsInDocument)
            {
                WarningInRevitModel warningModel = new WarningInRevitModel()
                {
                    AdditionalElements = item.GetAdditionalElements().Select(x => x.Value).ToList(),
                    FailingElements = item.GetFailingElements().Select(x => x.Value).ToList(),
                    WarningDescription = item.GetDescriptionText(),
                };
                warningInRevitModels.Add(warningModel);
            }

            string json = JsonUtils.BuildResponce(nameof(GetWarningsInRevit), warningInRevitModels);

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private record WarningInRevitModel
        {
            public string WarningDescription { get; set; }
            public List<long> FailingElements { get; set; }
            public List<long> AdditionalElements { get; set; }
        }
    }
}
