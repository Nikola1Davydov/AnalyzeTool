using Autodesk.Revit.UI;

namespace AnalyseTool.Utils
{
    internal class ExternalEventHub
    {
        public static RevitExternalEvent RevitExternalEvent { get; private set; }
        public static ExternalEvent RevitEvent { get; private set; }

        public static void Initialize(UIApplication app)
        {
            RevitExternalEvent = new RevitExternalEvent();
            RevitEvent = ExternalEvent.Create(RevitExternalEvent);
        }
    }
}
