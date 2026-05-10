using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Common.Model;
using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Get
{
    internal class GetDataByCategoryName : IRevitTask
    {
        public async Task Execute(JToken payload, WebView2 webView)
        {
            UpdateDataParameterFilledEmptyPagePayload? data = payload.ToObject<UpdateDataParameterFilledEmptyPagePayload>();
            if (string.IsNullOrEmpty(data.CategoryName)) return;

            DataElementsCollectorService dataElementsCollectorService = new DataElementsCollectorService();
            IEnumerable<DataElement> dataModels = dataElementsCollectorService.GetAllElementsByCategory(Context.Document, data.CategoryName)?.ToList() ?? new List<DataElement>();
            
            string json = JsonUtils.BuildResponce(nameof(GetDataByCategoryName), dataModels);

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private record UpdateDataParameterFilledEmptyPagePayload
        {
            public string CategoryName { get; set; }
        }
    }
}