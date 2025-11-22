using AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab;
using AnalyseTool.Utils;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.RevitCommands.ParameterControl
{
    /// <summary>
    ///     External command entry point invoked from the Revit interface
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ParameterControlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Context.Init(commandData.Application);

            ExternalEventHub.Initialize(commandData.Application);

            MainView subview = HostBuilderHelper.GetService<MainView>();
            subview.Show();

            return Result.Succeeded;
        }
    }
}