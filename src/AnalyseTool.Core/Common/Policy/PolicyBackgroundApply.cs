using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using Serilog;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>
    /// The startup rule (design §2) made concrete: reading the machine policy file is the only policy
    /// work on the Revit startup path. Everything that touches the network or a synced folder — the
    /// company catalog, required extensions — runs here, on a background task the host starts
    /// after its bootstrap returned, with timeouts, and its result applies when ready.
    /// </summary>
    internal static class PolicyBackgroundApply
    {
        private static readonly object Gate = new();
        private static Task? _running;
        private static string? _lastError;
        private static DateTimeOffset? _lastCompleted;

        /// <summary>Kicks off one pass unless one is already running. Returns immediately.</summary>
        public static void Start(TimeSpan? delay = null)
        {
            // A joined seat whose policy is not cached yet is "present" only after the first fetch.
            if (!PolicyStore.Current.IsPresent && PolicyStore.Current.Membership is null) return;

            lock (Gate)
            {
                if (_running is { IsCompleted: false }) return;
                _running = Task.Run(() => RunAsync(delay ?? TimeSpan.FromSeconds(5)));
            }
        }

        public static (bool Running, DateTimeOffset? LastCompleted, string? LastError) Status()
        {
            lock (Gate) return (_running is { IsCompleted: false }, _lastCompleted, _lastError);
        }

        /// <summary>The fleet view of one seat (design §10): versions, extensions, join state.</summary>
        public static void ReportInventory()
        {
            try
            {
                PolicyState policy = PolicyStore.Current;
                var extensions = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                    .Select(d => (d.Manifest.Id, d.Manifest.Version, d.Zone.ToString().ToLowerInvariant(),
                                  ExtensionStateStore.IsEnabled(d.Manifest.Id)));
                Common.Telemetry.TelemetryEvents.Inventory(extensions,
                    joined: policy.Membership is not null,
                    organization: policy.Document.Organization?.Name,
                    machinePolicy: policy.Machine.IsPointer || policy.LayerOf("codeExecution") == "machine"
                                   || policy.LayerOf("extensions") == "machine" || policy.LayerOf("mcp") == "machine");
            }
            catch (Exception ex) { Log.Debug(ex, "Inventory event skipped"); }
        }

        private static async Task RunAsync(TimeSpan delay)
        {
            try
            {
                // Let Revit finish drawing its ribbon before any I/O.
                await Task.Delay(delay);
                using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));

                // The organization layer first: everything below reads the merged policy.
                bool policyChanged = await OrgPolicySource.RefreshAsync(cts.Token);
                await ExtensionSourceCatalog.RefreshPolicyCatalogAsync(cts.Token);
                bool changed = await RequiredExtensions.ApplyAsync(cts.Token) || policyChanged;

                if (changed)
                    CoreServices.ReloadExtensions(); // thread-safe; ribbon subscribers hop to the UI thread themselves

                ReportInventory();
                lock (Gate) { _lastError = null; _lastCompleted = DateTimeOffset.Now; }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Applying the organization policy in the background failed");
                lock (Gate) { _lastError = ex.Message; _lastCompleted = DateTimeOffset.Now; }
            }
        }
    }
}
