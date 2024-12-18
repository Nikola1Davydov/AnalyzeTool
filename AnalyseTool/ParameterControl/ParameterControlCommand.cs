using AnalyseTool.ParameterControl.Views;
using AnalyseTool.Resources.wpf;
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
            ProgramContex.Init(commandData.Application);
            SubViewAnalyseTool subview = Host.GetService<SubViewAnalyseTool>();
            BaseView view = new BaseView("Parameter check", subview);
            view.Show();

            return Result.Succeeded;
        }
    }
}