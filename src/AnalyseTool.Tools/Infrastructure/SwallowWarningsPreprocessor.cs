using Autodesk.Revit.DB;

namespace AnalyseTool.Infrastructure
{
    /// <summary>
    /// Failure preprocessor for write transactions: deletes all warnings so Revit never raises a MODAL
    /// warning dialog that would block the Revit thread (the classic cause of the silent UI hang). Errors
    /// are left untouched, so a genuinely invalid edit still rolls the transaction back.
    /// </summary>
    internal sealed class SwallowWarningsPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            failuresAccessor.DeleteAllWarnings();
            return FailureProcessingResult.Continue;
        }

        /// <summary>Attaches a warning-swallowing handler to a started transaction.</summary>
        public static void Apply(Transaction transaction)
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new SwallowWarningsPreprocessor());
            options.SetClearAfterRollback(true);
            transaction.SetFailureHandlingOptions(options);
        }
    }
}
