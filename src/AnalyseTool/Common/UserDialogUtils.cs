using System.Windows;

namespace AnalyseTool.Common
{
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
