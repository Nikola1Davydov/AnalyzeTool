using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Launcher.RevitCommands
{

    /// <summary>Ribbon "Settings" button — the plugin's own preferences (MCP, about).</summary>
    [Transaction(TransactionMode.Manual)]
    internal sealed class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => App.InvokeRibbon("OpenSettings", commandData.Application);
    }

    /// <summary>Ribbon "Extensions" button — the extension manager (installed, catalog, dev folders).</summary>
    [Transaction(TransactionMode.Manual)]
    internal sealed class ExtensionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => App.InvokeRibbon("OpenExtensions", commandData.Application);
    }
}
