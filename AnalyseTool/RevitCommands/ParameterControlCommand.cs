using AnalyseTool.RevitCommands.Commands;
using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.DataModel;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab;
using AnalyseTool.Utils;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System.Text.Json;

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
            Context.Init(commandData.Application);

            ExternalEventHub.Initialize(commandData.Application);

            MainView subview = HostBuilderHelper.GetService<MainView>();

            List<string> allCategories = DataElementsCollectorUtils.GetModelCategoriesNames(Context.Document);
            string? jsonCategories = JsonConvert.SerializeObject(allCategories);
            subview.webView.WebMessageReceived += (sender, args) =>
            {
                string json = args.WebMessageAsJson;
                string? test = JsonConvert.DeserializeObject<string>(json);
                if (test != null && test.Equals("readyMessage"))
                {
                    subview.webView.CoreWebView2.PostWebMessageAsString(jsonCategories);
                    return;
                }

                WebViewMessage? message = JsonConvert.DeserializeObject<WebViewMessage>(json);
                if (message == null) return;

                IRevitTask task = CommandsFactory.CreateRevitCommand(message.CommandsEnum);
                task.Execute(message.JsonData);
            };

            subview.ShowDialog();

            return Result.Succeeded;
        }
    }
}