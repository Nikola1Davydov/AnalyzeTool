using AnalyseTool.RevitCommands;
using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab;
using AnalyseTool.Services;
using AnalyseTool.Utils;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Interop;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace AnalyseTool
{
    /// <summary>
    ///     External command entry point invoked from the Revit interface
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ParameterControlCommand : IExternalCommand
    {
        private static Window _openedWindow;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen", Justification = "<Ausstehend>")]
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Context.Init(commandData.Application);

                ExternalEventHub.Initialize(commandData.Application);

                MainView window = new MainView();
                window.Closed += WindowClosed;
                window.webView.WebMessageReceived += (sender, args) =>
                {
                    string json = args.WebMessageAsJson;
                    WebViewMessage? request = JsonConvert.DeserializeObject<WebViewMessage>(json);

                    if (request == null || !string.Equals(request.Type, WebMessageTypeEnum.Request.ToString(), StringComparison.OrdinalIgnoreCase)) return;

                    IRevitTask task = CommandsFactory.CreateRevitCommand(request.Command);
                    task.Execute(request.Payload, window.webView);
                };

                Show(window);
            }
            catch (Exception ex)
            {
                UserDialogService.Error(ex.Message);
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