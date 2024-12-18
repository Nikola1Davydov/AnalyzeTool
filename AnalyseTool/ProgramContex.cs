using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;

namespace AnalyseTool
{
    public static class ProgramContex
    {
        public static Document doc;
        public static UIApplication uiapp;
        public static UIDocument uidoc;
        public static Application app;

        public static void Init(UIApplication uIApplication)
        {
            uiapp = uIApplication;
            uidoc = uiapp.ActiveUIDocument;
            doc = uidoc.Document;
            app = uiapp.Application;
        }
    }
}
