using AnalyseTool.DoorManager.View;
using AnalyseTool.Resources.wpf;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace AnalyseTool.DoorManager
{
    [Transaction(TransactionMode.Manual)]
    public class DoorManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            ProgramContex.Init(commandData.Application);
            DoorManagerView subview = Host.GetService<DoorManagerView>();
            BaseView view = new BaseView("Door Manager", subview);
            view.Show();

            return Result.Succeeded;
        }
    }
}
