using AnalyseTool.DoorManager.View;
using AnalyseTool.ParameterControl.Views;
using AnalyseTool.Resources.wpf;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
