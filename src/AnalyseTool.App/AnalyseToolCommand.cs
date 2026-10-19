using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.App.Common;
using AnalyseTool.App.Common.Bootstrap;
using AnalyseTool.App.Common.Transport;
using AnalyseTool.Core;
using AnalyseTool.Core.Common;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.App
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyseToolCommand : IExternalCommand
    {
        private static Window? _openedWindow;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen", Justification = "<Ausstehend>")]
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                AnalyseToolBootstrap.Initialize(commandData.Application);

                if (!WebView2Runtime.EnsureOrWarn())
                    return Result.Succeeded;

                // A second click brings the open window forward. It used to build a new window and
                // transport first, never show them, and leave the transport attached for good.
                if (_openedWindow != null)
                {
                    Bring(_openedWindow);
                    return Result.Succeeded;
                }

                MainWindow window = new MainWindow();

                // All commands (built-in + extensions) are dispatched via Sdk.IRevitTask.
                WebView2Transport transport = new WebView2Transport(window.webView, CoreServices.Queue);
                transport.Attach();
                window.Closed += (_, _) =>
                {
                    transport.Detach();
                    _openedWindow = null;
                };

                _openedWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                UserDialogUtils.Error(ex.Message);
            }
            return Result.Succeeded;
        }

        private static void Bring(Window window)
        {
            window.Activate();
            window.WindowState = WindowState.Normal;
            CenterWindow(window);
        }
        private static void CenterWindow(Window window)
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
