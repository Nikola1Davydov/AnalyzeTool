using AnalyseTool.RevitCommands.Commands.Base;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class SelectionInRevit : IRevitTask
    {
        public void Execute(JToken data, WebView2 webView)
        {
            List<long>? list = data["elementIds"]?.ToObject<List<long>>() ?? new List<long>();

            if (list == null) return;

            List<ElementId> elementsIds = list.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            Context.UiDocument.Selection.SetElementIds(elementsIds);
        }
    }
}
