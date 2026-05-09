using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Common.FeaturesBase
{
    internal interface IRevitTask
    {
        Task Execute(JToken payload, WebView2 webView);
    }
}