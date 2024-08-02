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
            CreateRibbon();
        }

        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Davydov", "Analyse");

            panel.AddPushButton<StartupCommand>("Execute")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/icons8-analyse-34.ico");
        }
    }
}
