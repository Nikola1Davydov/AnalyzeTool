using AnalyseTool.Models;
using AnalyseTool.Utils;
using AnalyseTool.ViewModels;
using AnalyseTool.Views;
using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool
{
    /// <summary>
    ///     External command entry point invoked from the Revit interface
    /// </summary>
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            if (WindowController.Focus<AnalyseToolView>()) return;

            ProgramContex.Init(UiApplication);



        }
    }
}