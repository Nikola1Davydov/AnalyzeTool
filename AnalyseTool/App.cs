using AnalyseTool.AboutMe;
using AnalyseTool.ParameterControl;
using Autodesk.Revit.UI;
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
            HostBuilderHelper.StartHost();

            // Create a custom ribbon tab
            String tabName = "Analyse";
            application.CreateRibbonTab(tabName);

            // Add a new ribbon panel
            RibbonPanel ribbonPanel = application.CreateRibbonPanel(tabName, "Work with data");

            // Get dll assembly path
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Parameter Control button
            PushButtonData b1Data = new PushButtonData(
                nameof(ParameterControlCommand),
                "Parameter" + System.Environment.NewLine + "check",
                thisAssemblyPath,
                typeof(ParameterControlCommand).FullName);

            PushButton pb1 = ribbonPanel.AddItem(b1Data) as PushButton;
            BitmapImage pb1Image = new BitmapImage(new Uri("pack://application:,,,/AnalyseTool;component/Resources/Icons/AnalyzeTool_Icon.ico"));
            pb1.LargeImage = pb1Image;

            // About me button
            PushButtonData b2Data = new PushButtonData(
                nameof(AboutMeCommand),
                "About",
                thisAssemblyPath,
                typeof(AboutMeCommand).FullName);

            PushButton pb2 = ribbonPanel.AddItem(b2Data) as PushButton;
            BitmapImage pb2Image = new BitmapImage(new Uri("pack://application:,,,/AnalyseTool;component/Resources/Icons/AnalyzeTool_Icon.ico"));
            pb2.LargeImage = pb2Image;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
