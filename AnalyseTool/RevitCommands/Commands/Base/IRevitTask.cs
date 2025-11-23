using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands.Base
{
    internal interface IRevitTask
    {
        void Execute(object data, WebView2 webView);
    }
}