using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Serilog;

namespace AnalyseTool.Tools.Shared
{
    /// <summary>One Revit warning that a write transaction resolved on its own. Public because it rides
    /// in the public result records of the Families slice.</summary>
    /// <param name="Description">Revit's own description text, as the user would have read it in the dialog.</param>
    /// <param name="ElementIds">The elements Revit blamed; empty when the warning is document-wide.</param>
    public sealed record TransactionWarning(
        [property: JsonProperty("description")] string Description,
        [property: JsonProperty("elementIds")] IReadOnlyList<long> ElementIds);

    /// <summary>
    /// THE failure preprocessor for write transactions — every transaction in Tools attaches this one.
    /// It deletes warnings, so Revit never raises a MODAL dialog: one of those blocks the Revit thread
    /// inside <c>RevitTaskHub.Execute</c>, which is the single path to the model for EVERY transport, so
    /// a dialog nobody is watching freezes the whole tool.
    ///
    /// It RECORDS them before deleting. That is the difference from the swallowing handler this replaced
    /// (Families' <c>SwallowWarningsPreprocessor</c>, removed): discarding silently is defensible for a
    /// button a human is watching — they saw what they clicked — and in an unattended batch it throws
    /// away the only evidence that anything went sideways. Callers surface <see cref="Warnings"/> in
    /// their result, so 500 resolved warnings leave 500 traces instead of none.
    ///
    /// Errors are left untouched, so a genuinely invalid edit still rolls the transaction back.
    /// </summary>
    internal sealed class CollectingFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly List<TransactionWarning> _warnings = new();

        /// <summary>Warnings resolved so far. Revit may call the preprocessor several times for one
        /// transaction, so this accumulates across calls rather than being replaced.</summary>
        public IReadOnlyList<TransactionWarning> Warnings => _warnings;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> messages = failuresAccessor.GetFailureMessages();

            // Only when something actually happened: Revit calls the preprocessor on EVERY commit,
            // including the overwhelming majority that raise nothing, so an unconditional line here
            // would bury the log in "0 message(s)" and make the interesting case harder to find.
            // The result carries the warnings to the caller; this carries them to whoever is triaging a
            // log after the fact, which is the only witness an unattended run leaves behind.
            if (messages.Count > 0)
                Log.Debug("Resolved failures in transaction '{Transaction}': {Count} message(s)",
                    failuresAccessor.GetTransactionName(), messages.Count);

            foreach (FailureMessageAccessor failure in messages)
            {
                // Errors are deliberately not recorded here: they are not resolved, they roll the
                // transaction back, and the caller learns about them through the thrown exception.
                if (failure.GetSeverity() != FailureSeverity.Warning) continue;

                _warnings.Add(new TransactionWarning(
                    failure.GetDescriptionText(),
                    failure.GetFailingElementIds().Select(id => id.Value).ToList()));
            }

            failuresAccessor.DeleteAllWarnings();
            return FailureProcessingResult.Continue;
        }

        /// <summary>Attaches a collecting handler to a STARTED transaction and returns it, so the caller
        /// can read <see cref="Warnings"/> after the commit.</summary>
        public static CollectingFailuresPreprocessor Apply(Transaction transaction)
        {
            CollectingFailuresPreprocessor preprocessor = new();

            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(preprocessor);
            options.SetClearAfterRollback(true);
            transaction.SetFailureHandlingOptions(options);

            return preprocessor;
        }
    }
}
