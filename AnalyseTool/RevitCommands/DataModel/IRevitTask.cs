using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.DataModel
{
    internal interface IRevitTask
    {
        void Execute(JToken payload, WebView2 webView);
    }

}