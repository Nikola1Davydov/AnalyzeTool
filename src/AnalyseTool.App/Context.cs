using Autodesk.Revit.UI;

namespace AnalyseTool.App
{
    /// <summary>Ambient Revit context for the HOST UI layer (windows, dock panes) only.
    /// Platform and feature commands must not use this — they get IRevitContext, and Core
    /// resolves the Revit version via CoreServices.RevitVersion.</summary>
    public class Context
    {
        public static UIApplication UiApplication { get; private set; }

        public static void Init(UIApplication uiApplication)
        {
            UiApplication = uiApplication;
        }
    }
}
