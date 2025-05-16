using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SiCadLauncher
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elementSet)
        {
            UIApplication uiapp = commandData.Application;

            TaskDialog.Show("test", this.GetType().Name + '\n' + this.GetType().Assembly.GetName().Name);

            return Result.Succeeded;
        }
    }
}