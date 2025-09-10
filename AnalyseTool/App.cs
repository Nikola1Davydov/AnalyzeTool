using AnalyseTool.AboutMe;
using AnalyseTool.DoorManager;
using AnalyseTool.ParameterControl;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    public class App : ExternalApplication
    {
        public override void OnStartup()
        {
            HostBuilderHelper.StartHost();
            CreateRibbon();
        }

        // todo: add localization
        private void CreateRibbon()
        {
            Autodesk.Revit.UI.RibbonPanel panel = Application.CreatePanel("Work with data", "Analyse");

            panel.AddPushButton<ParameterControlCommand>("Parameter check")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/ParameterControl_Icon.ico");

            panel.AddPushButton<AboutMeCommand>("About me")
                .SetLargeImage("/AnalyseTool;component/Resources/Icons/AnalyzeTool_Icon.ico");
        }

    }
}
