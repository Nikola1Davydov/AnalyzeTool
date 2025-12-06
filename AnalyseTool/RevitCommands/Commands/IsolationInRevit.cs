using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class IsolationInRevit : IRevitTask
    {
        public void Execute(JToken data, WebView2 webView)
        {
            
            IsolationPayload? list = data.ToObject<IsolationPayload>();
            if (list == null) return;

            List<ElementId> elementsIds = list.ElementIds.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            if (!elementsIds.Any()) return;

            string transactionName = "Isolate";
            ExternalEventHub.RevitExternalEvent.action = () =>
            {
                RevitTransactions.Run(transactionName, () =>
                {
                    if (!Context.Document.ActiveView.IsModifiable) return;

                    if(Context.Document.ActiveView.IsTemporaryHideIsolateActive())
                    {
                        Context.Document.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    }
                    Context.Document.ActiveView.IsolateElementsTemporary(elementsIds);
                });
            };
            
            ExternalEventHub.RevitEvent.Raise();
        }
        private record IsolationPayload
        {
            public List<long> ElementIds { get; set; }
        }
    }
}
