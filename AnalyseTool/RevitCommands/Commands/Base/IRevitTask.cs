using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands.Base
{
    internal interface IRevitTask
    {
        Task Execute(JToken payload, WebView2 webView);
    }
}