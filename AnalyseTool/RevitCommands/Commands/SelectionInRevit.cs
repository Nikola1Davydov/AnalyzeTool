using AnalyseTool.RevitCommands.Commands.Base;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class SelectionInRevit : IRevitTask
    {
        public void Execute(object data)
        {
            List<long?> list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<long?>>(data.ToString());
            if (list == null) return;

            List<ElementId> elementsIds = list.Where(x => x != null).Select(x => new ElementId((long)x)).ToList();
            Context.UiDocument.Selection.SetElementIds(elementsIds);
        }
    }
}
