using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class IsolationInRevit : IRevitTask
    {
        public void Execute(JObject data)
        {
            List<long?>? list = JsonConvert.DeserializeObject<List<long?>>(data.ToString());
            if (list == null) return;

            List<ElementId> elementsIds = list.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            if (!elementsIds.Any()) return;

            //ExternalEventHub.RevitExternalEvent.action = () =>
            //{
            RevitTransactions.Run(() =>
            {
                Context.Document.ActiveView.IsolateElementsTemporary(elementsIds);
            });
            //};
            //ExternalEventHub.RevitExternalEvent.TransactionName = "Isolate";
            //ExternalEventHub.RevitEvent.Raise();
        }
    }
}
