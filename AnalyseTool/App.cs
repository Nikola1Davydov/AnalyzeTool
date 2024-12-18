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
            Host.Start();
            CreateRibbon();
        }
        public override void OnShutdown()
        {
            Host.Stop();
        }
        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Data Check", "Analyse");

            panel.AddPushButton<ParameterControlCommand>("Parameter Check")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/icons8-analyse-34.ico");

            panel.AddPushButton<DoorManagerCommand>("Door Manager")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/icons8-analyse-34.ico");
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
