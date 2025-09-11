using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool
{
    internal class Context
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
