using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Common.Model;
using AnalyseTool.Infrastructure;
using AnalyseTool.Utils;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Get
{
    internal class GetCategoriesInRevit : IRevitTask
    {
        public async Task Execute(JToken data, WebView2 webView)
        {
            DataElementsCollectorService dataElementsCollectorService = new DataElementsCollectorService();
            List<string> allCategories = await Task.Run(() => dataElementsCollectorService.GetModelCategoriesNames(Context.Document));

            string json = JsonUtils.BuildResponce(nameof(GetCategoriesInRevit), allCategories);

            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
