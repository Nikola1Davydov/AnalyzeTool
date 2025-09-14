using AnalyseTool.RevitCommands.ParameterControl;
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

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
        private void LoadDll()
        {
            string directory = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string meterialDesign = "MaterialDesignColors.dll";
            string meterialDesignXaml = "MaterialDesignThemes.Wpf.dll";

            string meterialDesignPath = Path.Combine(Path.GetDirectoryName(directory), meterialDesign);
            string meterialDesignXamlPath = Path.Combine(Path.GetDirectoryName(directory), meterialDesignXaml);

            Assembly.LoadFile(meterialDesignPath);
            Assembly.LoadFile(meterialDesignXamlPath);
        }
    }
}
