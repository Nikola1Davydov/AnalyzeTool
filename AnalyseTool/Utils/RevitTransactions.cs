using AnalyseTool.Common.FeaturesBase;
using AnalyseTool.Features.Actions;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;

namespace AnalyseTool.Utils
{
    internal class RevitTransactions
    {
        public static void Run(string TransactionName, WebView2 webView, Action action)
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
                    WebViewErrorHelper.SendError(webView, nameof(SetDataToParameters), ex.Message);
                }
            }
        }
    }
}
