using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class IsolationInRevit : IRevitTask
    {
        public void Execute(JToken data, WebView2 webView)
        {
            List<long?>? list = JsonConvert.DeserializeObject<List<long?>>(data.ToString());
            if (list == null) return;

            List<ElementId> elementsIds = list.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            if (!elementsIds.Any()) return;

            string transactionName = "Isolate";
            ExternalEventHub.RevitExternalEvent.action = () =>
            {
                RevitTransactions.Run(transactionName, () =>
                {
                    Context.Document.ActiveView.IsolateElementsTemporary(elementsIds);
                });
            };
            
            ExternalEventHub.RevitEvent.Raise();
        }
    }
}
