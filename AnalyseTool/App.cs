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
            string meterialDesign = "MaterialDesignColors.dll";
            string meterialDesignXaml = "MaterialDesignThemes.Wpf.dll";
            string liveCharts = "LiveCharts.dll";
            string liveChartsWpf = "LiveCharts.Wpf.dll";

            string meterialDesignPath = Path.Combine(Path.GetDirectoryName(directory), meterialDesign);
            string meterialDesignXamlPath = Path.Combine(Path.GetDirectoryName(directory), meterialDesignXaml);
            string liveChartsPath = Path.Combine(Path.GetDirectoryName(directory), liveCharts);
            string liveChartsWpfPath = Path.Combine(Path.GetDirectoryName(directory), liveChartsWpf);

            Assembly.LoadFile(meterialDesignPath);
            Assembly.LoadFile(meterialDesignXamlPath);
            Assembly.LoadFile(liveChartsPath);
            Assembly.LoadFile(liveChartsWpfPath);
        }
    }
}
