using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Runtime.Loader;

namespace SiCadLauncher
{
    public class AppTest : IExternalApplication
    {
        private IExternalApplication _loadedExternalApplication { get; set; }
        private AssemblyLoadContext _pluginContext;
        private const string LoadedAppClassName = "SiCadAddIn.App";
        private const string AssemblyName = "SiCadAddIn.dll";

        public Result OnStartup(UIControlledApplication uIApp)
        {
            try
            {
                string assemblyPath = getAssemblyPath();
                //ReloadAssembly(uIApp);
                //RuntimeCompiler reloadAssembly = new RuntimeCompiler();
                //Assembly assembly = reloadAssembly.CreateCommand();
                Assembly assembly = LoadFromMemory(assemblyPath);

                _loadedExternalApplication = (IExternalApplication)assembly.CreateInstance(LoadedAppClassName);

                return _loadedExternalApplication.OnStartup(uIApp);
                //return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to load SiCadAddIn: {ex.Message}");
                return Result.Failed;
            }

            //return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication uIApp)
        {
            try
            {
                if (_loadedExternalApplication != null)
                {
                    var result = _loadedExternalApplication.OnShutdown(uIApp);
                    _pluginContext?.Unload();
                    _pluginContext = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

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
        private void ReloadAssembly(UIControlledApplication uIApp)
        {
            string assemblyPath = getAssemblyPath();

            // Выгружаем предыдущую версию DLL, если контекст уже существует
            if (_pluginContext != null)
            {
                _pluginContext.Unload();
                _pluginContext = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            // Создаём новый контекст загрузки
            _pluginContext = new AssemblyLoadContext("PluginContext", isCollectible: true);

            // Загружаем новую версию DLL
            Assembly assembly = _pluginContext.LoadFromAssemblyPath(assemblyPath);

            // Создаём экземпляр нового приложения
            _loadedExternalApplication = (IExternalApplication)assembly.CreateInstance(LoadedAppClassName);

            // Инициализируем новое приложение
            _loadedExternalApplication.OnStartup(uIApp);
        }
        private static string getAssemblyPath()
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
        public Assembly LoadFromMemory(string dllPath)
        {
            byte[] assemblyBytes = File.ReadAllBytes(dllPath);
            return Assembly.Load(assemblyBytes);
        }
    }
}

