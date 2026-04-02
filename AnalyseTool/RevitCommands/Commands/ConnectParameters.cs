using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using ParameterUtils = Autodesk.Revit.DB.ParameterUtils;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class ConnectParameters : IRevitTask
    {
        public void Execute(JToken payload, WebView2 webView)
        {
            ApplyCombinedParametersPayload? list = payload.ToObject<ApplyCombinedParametersPayload>();
            if (list == null) return;

            foreach (var item in list.Items)
            {
                if (item == null) continue;

                var revitElement = Context.Document.GetElement(new ElementId(item.ElementId));
                var revitParameter = new ElementId(item.Id);
                ParameterUtils.IsBuiltInParameter(new ElementId(item.Id));

                revitElement.get_Parameter()

            }

            string transactionName = "Connect Parameters Execute";
            ExternalEventHub.RevitExternalEvent.action = () =>
            {
                RevitTransactions.Run(transactionName, () =>
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
        private record ApplyCombinedParametersPayload()
        {
            internal string CategoryName { get; set; }
            internal string TargetParameterName { get; set; }
            internal string Mode { get; set; }
            internal IEnumerable<CombineRulePayload> Rules { get; set; }
            internal IEnumerable<ParameterData> Items { get; set; }
        }
        private record CombineRulePayload()
        {
            internal string Kind { get; set; }
            internal string Value { get; set; }
            internal int Order { get; set; }
        }
    }
}
