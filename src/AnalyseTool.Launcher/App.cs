using Autodesk.Revit.UI;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Launcher
{
    public class App : IExternalApplication
    {
        public static Assembly? _pluginAssembly;

        // Preferred lookup: the current FQN. If a refactor moves/renames the type again, the
        // simple-name fallback in ResolveRibbonHost still finds it — and if even that fails we
        // show a TaskDialog instead of silently building no ribbon (GetType returns null and
        // `?.Invoke` swallows it, which once shipped a "plugin loads but nothing appears" state).
        private const string RibbonHostType = "AnalyseTool.App.Common.Extensions.RibbonHost";
        private const string RibbonHostSimpleName = "RibbonHost";

        public Result OnStartup(UIControlledApplication application)
        {
            LoadIsolatedDlls();

            // The ribbon (including dynamic extension buttons) is built by RibbonHost inside the
            // isolated AnalyseTool assembly; we just forward the call and hand it our assembly path
            // so it can point button commands at the slot classes Revit can load from this DLL.
            InvokeRibbon("Build", application, typeof(App).Assembly.Location);

            return Result.Succeeded;
        }

        /// <summary>Reflects into RibbonHost (isolated AnalyseTool.App) — the only way slot/Settings/Reload
        /// commands, which Revit loads from this Launcher DLL, can reach the plugin's logic.</summary>
        internal static Result InvokeRibbon(string method, params object[] args)
        {
            Type? ribbonHost = ResolveRibbonHost();
            if (ribbonHost is null)
            {
                TaskDialog.Show(SharedData.ToolData.PLUGIN_NAME,
                    $"Internal error: type '{RibbonHostType}' was not found in the plugin assembly. " +
                    "The ribbon cannot be built — please report this bug.");
                return Result.Failed;
            }

            MethodInfo? target = ribbonHost.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            if (target is null)
            {
                TaskDialog.Show(SharedData.ToolData.PLUGIN_NAME,
                    $"Internal error: method '{method}' was not found on '{ribbonHost.FullName}'. " +
                    "Please report this bug.");
                return Result.Failed;
            }

            target.Invoke(null, args);
            return Result.Succeeded;
        }

        /// <summary>FQN first; falls back to a simple-name scan so a namespace refactor in the plugin
        /// assembly cannot silently disconnect the Launcher.</summary>
        private static Type? ResolveRibbonHost()
        {
            if (_pluginAssembly is null) return null;
            Type? byFullName = _pluginAssembly.GetType(RibbonHostType);
            if (byFullName is not null) return byFullName;

            try
            {
                return _pluginAssembly.GetTypes()
                    .FirstOrDefault(t => string.Equals(t.Name, RibbonHostSimpleName, StringComparison.Ordinal));
            }
            catch
            {
                return null;
            }
        }

        private static void LoadIsolatedDlls()
        {
            string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string basePath = Path.Combine(pluginFolder, "AnalyseTool.App.dll");

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
