using AnalyseTool.RevitCommands.DataModel;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class SelectionInRevit : IRevitTask
    {
        public void Execute(JToken data, WebView2 webView)
        {
            SelectionPayload? list = data.ToObject<SelectionPayload>();

            if (list == null) return;

            List<ElementId> elementsIds = list.ElementIds.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            Context.UiDocument.Selection.SetElementIds(elementsIds);
        }
        private record SelectionPayload
        {
            public List<long> ElementIds { get; set; }
        }
    }
}
