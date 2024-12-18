using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace AnalyseTool
{
    /// <summary>
    ///     External command entry point invoked from the Revit interface
    /// </summary>
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            ProgramContex.Init(commandData.Application);
            var view = Host.GetService<AnalyseToolView>();
            view.Show();

            return Result.Succeeded;
        }
    }
}