using AnalyseTool.RevitCommands.Commands.Base;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class SelectionInRevit : IRevitTask
    {
        public void Execute(object data, WebView2 webView)
        {
            List<long?>? list = JsonSerializer.Deserialize<List<long?>>(data.ToString());
            if (list == null) return;

            List<ElementId> elementsIds = list.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            Context.UiDocument.Selection.SetElementIds(elementsIds);
        }
    }
}
