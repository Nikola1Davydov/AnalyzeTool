using Serilog;
using System.IO;

namespace AnalyseTool.Common
{
    /// <summary>
    /// One-time setup of the Serilog file logger used across the plugin. Writes a daily rolling log to
    /// <c>%LOCALAPPDATA%\AnalyseTool\logs\analysetool-&lt;date&gt;.log</c> so beta issues can be triaged from
    /// the user's machine. Entirely best-effort — logging must never crash the add-in.
    /// </summary>
    internal static class AppLog
    {
        private static bool _initialized;
        private static readonly object Gate = new();

        public static void Initialize()
        {
            if (_initialized) return;
            lock (Gate)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    string logDir = Path.Combine(PathProvider.ProfilePath, "logs");
                    Directory.CreateDirectory(logDir);

                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.File(
                            path: Path.Combine(logDir, "analysetool-.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14,
                            shared: true,
                            flushToDiskInterval: TimeSpan.FromSeconds(1),
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                        .CreateLogger();

                    // Last-resort capture of crashes anywhere in the process.
                    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating={Terminating})", e.IsTerminating);

                    string version = SharedData.ToolData.PLUGIN_VERSION;
                    Log.Information("==== AnalyseTool {Version} logging started ====", version);
                }
                catch
                {
                    // No logging available — swallow; the plugin keeps working.
                }
            }
        }

        /// <summary>Flushes and closes the logger (call on Revit shutdown).</summary>
        public static void Shutdown()
        {
            try { Log.CloseAndFlush(); }
            catch { /* best-effort */ }
        }
    }
}
