using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace AnalyseTool.RevitCommands
{
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
