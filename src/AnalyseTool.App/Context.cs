using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace AnalyseTool.App
{
    /// <summary>Ambient Revit context for the HOST UI layer (windows, dock panes) only.
    /// Platform and feature commands must not use this — they get IRevitContext, and Core
    /// resolves the Revit version via CoreServices.RevitVersion.</summary>
    public class Context
    {
        public static UIApplication UiApplication { get; private set; }
        public static Application Application => UiApplication.Application;

        public static UIDocument UiDocument => UiApplication.ActiveUIDocument;
        public static Document Document => UiDocument.Document;
        public static void Init(UIApplication uiApplication)
        {
            UiApplication = uiApplication;
        }
    }
}
