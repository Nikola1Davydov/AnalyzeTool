using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Features.Actions
{
    internal class IsolationInRevit : IRevitTask
    {
        public async Task Execute(JToken data, WebView2 webView)
        {

            IsolationPayload? list = data.ToObject<IsolationPayload>();
            if (list == null) return;

            List<ElementId> elementsIds = list.ElementIds.Where(x => x != null).Select(x => new ElementId(x)).ToList();
            if (!elementsIds.Any()) return;

            string transactionName = "Isolate";
            ExternalEventHub.RevitExternalEvent.action = () =>
            {
                RevitTransactions.Run(transactionName, webView, () =>
                {
                    if (!Context.Document.ActiveView.IsModifiable) return;

                    if (Context.Document.ActiveView.IsTemporaryHideIsolateActive())
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
