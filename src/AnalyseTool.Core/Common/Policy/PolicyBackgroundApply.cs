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
            if (!PolicyStore.Current.IsPresent) return; // no policy, nothing to apply — single-seat behavior

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

        private static async Task RunAsync(TimeSpan delay)
        {
            try
            {
                // Let Revit finish drawing its ribbon before any I/O.
                await Task.Delay(delay);
                using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));

                await ExtensionSourceCatalog.RefreshPolicyCatalogAsync(cts.Token);
                bool changed = await RequiredExtensions.ApplyAsync(cts.Token);

                if (changed)
                    CoreServices.ReloadExtensions(); // thread-safe; ribbon subscribers hop to the UI thread themselves

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
