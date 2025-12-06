using Autodesk.Revit.UI;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Media.Imaging;

namespace AnalyseTool.Launcher
{
    public class App : IExternalApplication
    {
        public static Assembly? _pluginAssembly;
        public Result OnStartup(UIControlledApplication application)
        {
            LoadIsolatedDlls();

            RegisterUI(application);

            return Result.Succeeded;
        }

        private static void LoadIsolatedDlls()
        {
            string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string basePath = Path.Combine(pluginFolder, "AnalyseTool.dll");

            AssemblyDependencyResolver resolver = new AssemblyDependencyResolver(typeof(App).Assembly.Location);
            IsolatedAssemblyLoadContent _isolatedContext = new IsolatedAssemblyLoadContent(SharedData.ToolData.PLUGIN_NAME, resolver);

            string[] dependencies = new[]
            {
                "Newtonsoft.Json.dll",
            };

            foreach (string dep in dependencies)
            {
                string depPath = Path.Combine(pluginFolder, dep);
                if (File.Exists(depPath))
                {
                    _isolatedContext.LoadFromAssemblyPath(depPath);
                }
            }

            _pluginAssembly = _isolatedContext.LoadFromAssemblyPath(basePath);
        }

        private void RegisterUI(UIControlledApplication application)
        {
            // Add a new ribbon panel
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("Parameter");

            PushButtonData b1Data = new PushButtonData(
                nameof(ParameterControlCommand),
                SharedData.ToolData.PLUGIN_NAME,
                GetType().Assembly!.Location,
                typeof(ParameterControlCommand).FullName);

            PushButton pb1 = ribbonPanel.AddItem(b1Data) as PushButton;
            BitmapImage pb1Image = new BitmapImage(new Uri("pack://application:,,,/AnalyseTool;component/Resources/Icons/AnalyzeTool_Icon.ico"));
            pb1.LargeImage = pb1Image;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
