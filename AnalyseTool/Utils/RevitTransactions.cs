using AnalyseTool.RevitCommands;
using AnalyseTool.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Utils
{
    internal class RevitTransactions
    {
        public static void Run(string TransactionName, Action action)
        {
            using (Transaction transaction = new Transaction(Context.Document, TransactionName))
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
                    UserDialogService.Error(ex.Message);
                }
            }
        }
    }
}
