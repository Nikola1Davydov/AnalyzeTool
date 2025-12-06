using Autodesk.Revit.UI;
using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Launcher
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string basePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "AnalyseTool.dll");

            AssemblyDependencyResolver resolver = new AssemblyDependencyResolver(typeof(App).Assembly.Location);
            IsolatedAssemblyLoadContent isolatedAssembly = new IsolatedAssemblyLoadContent(SharedData.ToolData.PLUGIN_NAME ,resolver);

            Assembly assembly = isolatedAssembly.LoadFromAssemblyPath(basePath);
            Type? appType = assembly.GetType("AnalyseTool.App");
            if (appType == null) return Result.Failed;

            IExternalApplication? externalApp = Activator.CreateInstance(appType) as IExternalApplication;
            if (externalApp == null) return Result.Failed;

            externalApp.OnStartup(application);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
