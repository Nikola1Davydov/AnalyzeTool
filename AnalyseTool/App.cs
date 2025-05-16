using AnalyseTool.DoorManager;
using AnalyseTool.ParameterControl;
using Nice3point.Revit.Toolkit.External;
using System.IO;
using System.Reflection;

namespace AnalyseTool
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    public class App : ExternalApplication
    {
        public override void OnStartup()
        {
            LoadDLL();
            CreateRibbon();
        }
        public override void OnShutdown()
        {

        }
        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Work with data", "Analyse");

            panel.AddPushButton<ParameterControlCommand>("Parameter check")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/ParameterControl_Icon.ico");

            panel.AddPushButton<DoorManagerCommand>("Door manager")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/DoorManager_Icon.ico");

            panel.AddPushButton<DoorManagerCommand>("About me")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/AnalyzeTool_Icon.ico");
        }
        private void LoadDLL()
        {
            // Получение пути к текущей DLL или EXE
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Получение пути к папке
            string wpfuiPath = Path.Combine(Path.GetDirectoryName(assemblyPath), "Wpf.Ui.dll");
            Assembly.LoadFrom(wpfuiPath);
        }
    }
}
