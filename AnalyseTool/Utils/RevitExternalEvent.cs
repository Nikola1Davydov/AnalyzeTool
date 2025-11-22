using Autodesk.Revit.UI;

namespace AnalyseTool.Utils
{
    internal class RevitExternalEvent : IExternalEventHandler
    {
        internal Action action;
        internal string TransactionName { get; set; }
        public void Execute(UIApplication app)
        {
            if (action == null) return;
            if (string.IsNullOrEmpty(TransactionName)) TransactionName = "AnalyseTool Method";

            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        public string GetName()
        {
            return TransactionName;
        }
    }
}
