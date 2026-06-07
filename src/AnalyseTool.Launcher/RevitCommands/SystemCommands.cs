using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Launcher.RevitCommands
{
    /// <summary>Ribbon "Settings" button — shows where extensions live and how to add them.</summary>
    [Transaction(TransactionMode.Manual)]
    internal sealed class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => App.InvokeRibbon("OpenSettings", commandData.Application);
    }

    /// <summary>Ribbon "Reload" button — reloads extensions.</summary>
    [Transaction(TransactionMode.Manual)]
    internal sealed class ReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => App.InvokeRibbon("Reload", commandData.Application);
    }
}
