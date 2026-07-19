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
            // FQN first, simple-name fallback — a namespace refactor in the plugin assembly must not
            // silently disconnect the main ribbon button (same pattern as App.ResolveRibbonHost).
            Type? commandType = App._pluginAssembly?.GetType("AnalyseTool.App.AnalyseToolCommand")
                ?? App._pluginAssembly?.GetTypes().FirstOrDefault(t => t.Name == "AnalyseToolCommand");
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
