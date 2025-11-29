using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab;
using AnalyseTool.Utils;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands
{
    /// <summary>
    ///     External command entry point invoked from the Revit interface
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ParameterControlCommand : IExternalCommand
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen", Justification = "<Ausstehend>")]
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Context.Init(commandData.Application);

                ExternalEventHub.Initialize(commandData.Application);

                MainView subview = HostBuilderHelper.GetService<MainView>();

                subview.webView.WebMessageReceived += (sender, args) =>
                {
                    string json = args.WebMessageAsJson;
                    WebViewMessage? request = JsonConvert.DeserializeObject<WebViewMessage>(json);

                    if (request == null || !string.Equals(request.Type, WebMessageTypeEnum.Request.ToString(), StringComparison.OrdinalIgnoreCase)) return;

                    IRevitTask task = CommandsFactory.CreateRevitCommand(request.Command);
                    task.Execute(request.Payload, subview.webView);
                };

                subview.Show();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
            return Result.Succeeded;
        }
    }
}