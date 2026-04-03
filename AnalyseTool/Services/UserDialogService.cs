using System.Windows;

namespace AnalyseTool.Services
{
    internal class UserDialogService
    {
        public static void ShowMessage(string message, string title = "Information")
        {
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        public static bool ShowConfirmation(string message, string title = "Confirmation")
        {
            MessageBoxResult result = System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }
        public static void Error(string message)
        {
            System.Windows.MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
