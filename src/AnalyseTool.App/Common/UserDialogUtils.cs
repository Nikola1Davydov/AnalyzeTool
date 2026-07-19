using System.Windows;

namespace AnalyseTool.App.Common
{
    /// <summary>Modal message boxes for the HOST UI layer. Core never shows dialogs — its errors go
    /// to the log and ExtensionDiagnostics (surfaced by the Settings listing), because Core also
    /// serves headless transports (MCP, future remote) where a modal dialog would hang the caller.</summary>
    internal class UserDialogUtils
    {
        public static void ShowMessage(string message, string title = "Information")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        public static bool ShowConfirmation(string message, string title = "Confirmation")
        {
            MessageBoxResult result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }
        public static void Error(string message)
        {
            // Every user-facing error is also logged, so beta issues leave a trace in the log file.
            try { Serilog.Log.Error("UserDialog: {Message}", message); } catch { /* logging is best-effort */ }
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
