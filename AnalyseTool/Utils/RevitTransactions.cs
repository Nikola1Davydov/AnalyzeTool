using AnalyseTool.RevitCommands;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Utils
{
    internal class RevitTransactions
    {
        public static void Run(Action action)
        {
            using (Transaction transaction = new Transaction(Context.Document))
            {
                try
                {
                    transaction.Start();

                    action.Invoke();         

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.RollBack();
                    TaskDialog.Show("Error", ex.Message);
                }
            }
        }
    }
}
