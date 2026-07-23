namespace AnalyseTool.Core.Common.Dispatch
{
    /// <summary>
    /// "Is Revit free right now?" — the RevitDBExplorer technique: the host keeps a permanent
    /// UIApplication.Idling subscription whose handler just stamps the time. While Revit is idle the
    /// event fires continuously, so a stale stamp means Revit is held by a modal dialog, an edit
    /// mode or a native command — detected within ~a second, with zero queue traffic. Core only
    /// stores the stamp; the subscription itself lives in the host bootstrap (it needs UIApplication).
    /// </summary>
    internal static class RevitAvailability
    {
        /// <summary>Revit hasn't idled for this long (seconds) → it is busy/blocked.</summary>
        public const double BusyThresholdSeconds = 1.5;

        private static long _lastIdleTicks;

        /// <summary>Called from the host's Idling handler (and once at bootstrap, which runs in a
        /// command context — Revit is momentarily "ours" there).</summary>
        public static void ReportIdle() =>
            Interlocked.Exchange(ref _lastIdleTicks, DateTime.UtcNow.Ticks);

        /// <summary>Seconds since Revit last reported idle; 0 while the stamp is fresh-unknown.</summary>
        public static double SecondsSinceIdle
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastIdleTicks);
                return ticks == 0 ? 0 : (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            }
        }

        public static bool IsRevitBusy => SecondsSinceIdle > BusyThresholdSeconds;
    }
}
