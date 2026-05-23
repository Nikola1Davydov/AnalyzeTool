using Autodesk.Revit.UI;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Launcher
{
    public class App : IExternalApplication
    {
        public static Assembly? _pluginAssembly;

        private const string RibbonHostType = "AnalyseTool.Infrastructure.Extensions.RibbonHost";

        public Result OnStartup(UIControlledApplication application)
        {
            LoadIsolatedDlls();

            // The ribbon (including dynamic extension buttons) is built by RibbonHost inside the
            // isolated AnalyseTool assembly; we just forward the call and hand it our assembly path
            // so it can point button commands at the slot classes Revit can load from this DLL.
            InvokeRibbon("Build", application, typeof(App).Assembly.Location);

            return Result.Succeeded;
        }

        /// <summary>Reflects into RibbonHost (isolated AnalyseTool) — the only way slot/Settings/Reload
        /// commands, which Revit loads from this Launcher DLL, can reach the plugin's logic.</summary>
        internal static Result InvokeRibbon(string method, params object[] args)
        {
            Type? ribbonHost = _pluginAssembly?.GetType(RibbonHostType);
            MethodInfo? target = ribbonHost?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            target?.Invoke(null, args);
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

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
