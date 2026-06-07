using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Launcher.RevitCommands
{
    [Transaction(TransactionMode.Manual)]
    internal class AnalyseToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Type? commandType = App._pluginAssembly?.GetType("AnalyseTool.AnalyseToolCommand");
            if (commandType == null)
            {
                TaskDialog.Show("Error", "Command not found");
                return Result.Failed;
            }

            IExternalCommand? command = Activator.CreateInstance(commandType) as IExternalCommand;
            return command?.Execute(commandData, ref message, elements) ?? Result.Failed;
        }
    }
}
