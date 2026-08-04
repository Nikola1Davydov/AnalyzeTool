using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Serilog;

namespace AnalyseTool.App.Common
{
    /// <summary>
    /// TEMPORARY INSTRUMENTATION — answers the two open questions in issue #88, then goes away
    /// (`git revert` the commit that added it). Nothing in the product depends on it, and it changes
    /// no behaviour unless <c>ANALYSETOOL_EXP88_RESOLVE</c> is set.
    ///
    /// <para><b>Q1 — coverage.</b> Does an application-level subscription see a Revit failure raised
    /// inside a transaction WE DID NOT OPEN (a third-party command, a Roslyn snippet run through
    /// ExecuteRevitCode)? If yes, unattended-run safety can be a host guarantee instead of a rule
    /// extension authors have to remember. If no, the backstop described in the design doc cannot
    /// exist in that form and #88 needs a different answer.</para>
    ///
    /// <para><b>Q2 — can it be resolved from there, or only observed?</b> Set
    /// <c>ANALYSETOOL_EXP88_RESOLVE=1</c> before starting Revit and the handler tries to delete the
    /// warnings and continue. Leave it unset (the default) to only watch — which also leaves the
    /// dialog on screen, that being exactly the state the re-entrancy experiment needs.</para>
    ///
    /// <para>Ordering is part of the answer too: run the provocation once with a per-transaction
    /// preprocessor attached and once without, and compare which handler logs what.</para>
    ///
    /// <para>Everything lands in <c>%LOCALAPPDATA%\AnalyseTool\logs\analysetool-&lt;date&gt;.log</c>
    /// at Warning level, prefixed <c>EXP88</c>, so <c>Select-String EXP88</c> is the whole readout.</para>
    /// </summary>
    internal static class ExperimentalFailureProbe
    {
        private static readonly bool TryResolve =
            Environment.GetEnvironmentVariable("ANALYSETOOL_EXP88_RESOLVE") == "1";

        /// <summary>Call from a valid API context (OnStartup). Failures to subscribe are themselves a
        /// result — which of these events is reachable from here is part of what Q1 asks.</summary>
        public static void Attach(UIControlledApplication app)
        {
            try
            {
                app.DialogBoxShowing += OnDialogBoxShowing;
                app.ControlledApplication.FailuresProcessing += OnFailuresProcessing;
                Log.Warning("EXP88: probe attached (resolve={Resolve}). TEMPORARY instrumentation — see issue #88.",
                    TryResolve);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EXP88: could not attach the probe");
            }
        }

        private static void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
        {
            // The concrete args type matters: TaskDialogShowingEventArgs and MessageBoxShowingEventArgs
            // carry different ways to answer, and a failure dialog may not arrive as either.
            Log.Warning("EXP88 DialogBoxShowing: id={DialogId} argsType={Type} thread={Thread}",
                e.DialogId, e.GetType().Name, Environment.CurrentManagedThreadId);
        }

        private static void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor accessor = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> messages = accessor.GetFailureMessages();

            Log.Warning("EXP88 FailuresProcessing: transaction='{Transaction}' count={Count} thread={Thread}",
                accessor.GetTransactionName(), messages.Count, Environment.CurrentManagedThreadId);

            foreach (FailureMessageAccessor message in messages)
                Log.Warning("EXP88   {Severity}: {Text}", message.GetSeverity(), message.GetDescriptionText());

            if (!TryResolve) return;

            accessor.DeleteAllWarnings();
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
            Log.Warning("EXP88: deleted the warnings from the APPLICATION-level handler and set ProceedWithCommit");
        }
    }
}
