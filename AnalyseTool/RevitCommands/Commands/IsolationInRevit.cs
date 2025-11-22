using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class IsolationInRevit : IRevitTask
    {
        public void Execute(object data)
        {
            List<long?> list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<long?>>(data.ToString());
            if (list == null) return;

            List<ElementId> elementsIds = list.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            if (!elementsIds.Any()) return;

            ExternalEventHub.RevitExternalEvent.action = () =>
            {
                Context.Document.ActiveView.IsolateElementsTemporary(elementsIds);
            };
            ExternalEventHub.RevitExternalEvent.TransactionName = "Isolate";
            ExternalEventHub.RevitEvent.Raise();
        }
    }
}
