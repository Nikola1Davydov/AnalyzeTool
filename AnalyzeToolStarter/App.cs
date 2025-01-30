using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace AnalyzeToolStarter
{
    public class App : IExternalApplication
    {
        private IExternalApplication _loadedExternalApplication { get; set; }
        public static RibbonPanel ribbonPanel;
        //private const string LoadedAppClassName = "AnalyseTool.App";
        private const string AssemblyName = "AnalyseTool.dll";
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                //Assembly assembly = 
                //string assemblyPath = getAssemblyPath();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to load AnalyseTool: {ex.Message}");
                return Result.Failed;
            }
        }
        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (_loadedExternalApplication != null)
                {
                    var result = _loadedExternalApplication.OnShutdown(application);

                    return result;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to unload AnalyseTool: {ex.Message}");
                return Result.Failed;
            }
        }

        private string getAssemblyPath()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string directoryPath = Path.GetDirectoryName(assemblyPath);
            string fullPath = Path.Combine(directoryPath, AssemblyName);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Assembly '{AssemblyName}' not found at '{fullPath}'.");
            }

            return fullPath;
        }
    }
}
