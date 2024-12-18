using Nice3point.Revit.Toolkit.External;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AnalyseTool
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class App : ExternalApplication
    {
        public override void OnStartup()
        {
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

            panel.AddPushButton<StartupCommand>("Parameter Check")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/icons8-analyse-34.ico");
        }
    }
}
