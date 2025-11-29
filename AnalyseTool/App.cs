using AnalyseTool.RevitCommands;
using Autodesk.Revit.UI;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace AnalyseTool
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    public class App : IExternalApplication
    {
        private UIControlledApplication Application;
        public Result OnStartup(UIControlledApplication application)
        {
            Application = application;
            LoadDll();
            HostBuilderHelper.StartHost();

            // Add a new ribbon panel
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("Parameter");

            // Get dll assembly path
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Parameter Control button
            PushButtonData b1Data = new PushButtonData(
                nameof(ParameterControlCommand),
                SharedData.ToolData.PLUGIN_NAME,
                thisAssemblyPath,
                typeof(ParameterControlCommand).FullName);

            PushButton pb1 = ribbonPanel.AddItem(b1Data) as PushButton;
            BitmapImage pb1Image = new BitmapImage(new Uri("pack://application:,,,/AnalyseTool;component/Resources/Icons/AnalyzeTool_Icon.ico"));
            pb1.LargeImage = pb1Image;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
        private void LoadDll()
        {
            string directory = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string json = "Newtonsoft.Json.dll";

            string jsonPath = Path.Combine(Path.GetDirectoryName(directory), json);

            Assembly.LoadFile(jsonPath);
        }
    }
}
