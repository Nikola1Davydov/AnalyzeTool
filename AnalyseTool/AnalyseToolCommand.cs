using AnalyseTool.Infrastructure.Bootstrap;
using AnalyseTool.Infrastructure.Transport;
using AnalyseTool.Utils;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyseToolCommand : IExternalCommand
    {
        private static Window _openedWindow;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen", Justification = "<Ausstehend>")]
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                AnalyseToolBootstrap.Initialize(commandData.Application);

                MainWindow window = new MainWindow();
                window.Closed += WindowClosed;

                // All commands (built-in + extensions) are dispatched via Sdk.IRevitTask.
                WebView2Transport transport = new WebView2Transport(window.webView, AnalyseToolBootstrap.Dispatcher);
                transport.Attach();

                Show(window);
            }
            catch (Exception ex)
            {
                UserDialogUtils.Error(ex.Message);
            }
            return Result.Succeeded;
        }

        private void WindowClosed(object? sender, EventArgs e)
        {
            _openedWindow = null;
        }
        private void Show(Window window)
        {
            WindowInteropHelper helper = new WindowInteropHelper(window);
            helper.Owner = Context.UiApplication.MainWindowHandle;

            if (_openedWindow != null)
            {
                _openedWindow.Activate();
                _openedWindow.WindowState = WindowState.Normal;

                CenterWindow(_openedWindow);
            }
            else
            {
                _openedWindow = window;
                window.Show();
            }
        }
        private void CenterWindow(Window window)
        {
            if (window.Owner != null)
            {
                window.Left = window.Owner.Left + (window.Owner.Width - window.Width) / 2;
                window.Top = window.Owner.Top + (window.Owner.Height - window.Height) / 2;
            }
            else
            {
                window.Left = (SystemParameters.PrimaryScreenWidth - window.Width) / 2;
                window.Top = (SystemParameters.PrimaryScreenHeight - window.Height) / 2;
            }
        }
    }
}