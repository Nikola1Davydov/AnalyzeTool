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

    /// <summary>Ribbon "Report a bug" button — opens the GitHub issues page in the browser. Handled here
    /// in the Launcher (not the host) so it works even if the plugin failed to load.</summary>
    [Transaction(TransactionMode.Manual)]
    internal sealed class BugsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string url = SharedData.ToolData.LINK_TO_GITHUB.TrimEnd('/') + "/issues";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* best-effort */ }
            return Result.Succeeded;
        }
    }
}
