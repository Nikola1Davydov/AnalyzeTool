using AnalyseTool.Services;
using Autodesk.Revit.UI;

namespace AnalyseTool.Utils
{
    internal class RevitExternalEvent : IExternalEventHandler
    {
        internal Action action;
        public void Execute(UIApplication app)
        {
            if (action == null) return;

            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                UserDialogService.Error(ex.Message);
            }
        }

        public string GetName()
        {
            return "TransactionName";
        }
    }
}
