using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using System.IO;

namespace AnalyseTool.App.Common
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

                    const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
                    LoggerConfiguration config = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.File(
                            path: Path.Combine(logDir, "analysetool-.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14,
                            shared: true,
                            flushToDiskInterval: TimeSpan.FromSeconds(1),
                            outputTemplate: template);

                    Log.Logger = config.CreateLogger();

                    // The organization policy's additional sink (design §2, "logging"): a folder or file
                    // path on a share, {user}/{machine} expanded, one file per day. The local file stays.
                    // Attached OFF the startup path: opening a file on a share is a network call, and an
                    // unreachable share must cost a background task a timeout, not the ribbon.
                    string? policySink = PolicyLogSink(out LogEventLevel policyLevel);
                    if (policySink is not null)
                        _ = Task.Run(() => AttachPolicySink(policySink, policyLevel, template));

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

        private static void AttachPolicySink(string policySink, LogEventLevel level, string template)
        {
            try
            {
                string? folder = Path.GetDirectoryName(policySink);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) // the probe that may hang: here, not on the UI thread
                {
                    Log.Warning("Organization log sink folder not reachable: {Sink}", policySink);
                    return;
                }
                string logDir = Path.Combine(PathProvider.ProfilePath, "logs");
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        path: Path.Combine(logDir, "analysetool-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        shared: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(1),
                        outputTemplate: template)
                    .WriteTo.File(
                        path: policySink,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31,
                        shared: true,
                        restrictedToMinimumLevel: level,
                        flushToDiskInterval: TimeSpan.FromSeconds(5),
                        outputTemplate: template)
                    .CreateLogger();
                Log.Information("Organization log sink attached: {Sink} ({Level}+)", policySink, level);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Organization log sink could not be attached: {Sink}", policySink);
            }
        }

        /// <summary>Reads <c>logging.sink</c> / <c>logging.level</c> from the policy. Null when absent
        /// or unusable — the policy must never keep the plugin from logging locally.</summary>
        internal static string? PolicyLogSink(out LogEventLevel level)
        {
            level = LogEventLevel.Information;
            try
            {
                JObject? logging = PolicyStore.Current.IsPresent ? PolicyStore.Current.Document.Logging : null;
                string? sink = logging?["sink"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(sink)) return null;
                if (Enum.TryParse(logging?["level"]?.Value<string>(), ignoreCase: true, out LogEventLevel parsed)) level = parsed;
                return ExpandSink(sink);
            }
            catch { return null; }
        }

        /// <summary>Pure: {user} / {machine} expanded, %ENV% expanded, a folder turned into a file
        /// pattern, and the rolling logger's own date suffix used for {date}.</summary>
        internal static string ExpandSink(string sink)
        {
            string s = Environment.ExpandEnvironmentVariables(sink.Trim())
                .Replace("{user}", Environment.UserName, StringComparison.OrdinalIgnoreCase)
                .Replace("{machine}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                .Replace("{date}", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (s.EndsWith(Path.DirectorySeparatorChar) || s.EndsWith('/') || !Path.HasExtension(s))
                s = Path.Combine(s, "analysetool-.log");
            else if (Path.GetFileName(s).StartsWith('.')) // "{date}.log" became ".log"
                s = Path.Combine(Path.GetDirectoryName(s) ?? string.Empty, "analysetool-" + Path.GetFileName(s));
            return s;
        }

        /// <summary>Flushes and closes the logger (call on Revit shutdown).</summary>
        public static void Shutdown()
        {
            try { Log.CloseAndFlush(); }
            catch { /* best-effort */ }
        }
    }
}
