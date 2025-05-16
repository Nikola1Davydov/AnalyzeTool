using AnalyseTool.ParameterControl.Views;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace AnalyseTool.ParameterControl
{
    /// <summary>
    ///     External command entry point invoked from the Revit interface
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ParameterControlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //var te = commandData.Application;
            //IAnalyseToolModel model = new AnalyseToolModel(commandData.Application);
            //AnalyseToolViewModel analyseToolViewModel = new AnalyseToolViewModel(model);
            HostBuilderHelper.StartHost();
            AnalyseToolView subview = HostBuilderHelper.GetService<AnalyseToolView>();
            subview.Show();

            return Result.Succeeded;
        }
    }
}