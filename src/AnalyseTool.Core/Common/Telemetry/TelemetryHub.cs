using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AnalyseTool.Core.Common.Telemetry
{
    /// <summary>The <c>telemetry</c> policy section (design §10).</summary>
    internal sealed class TelemetryPolicy
    {
        /// <summary>An https endpoint that accepts newline-delimited JSON (Seq's raw ingestion, any
        /// collector with an NDJSON input), or a folder (share, synced library) for JSON-lines files.</summary>
        [JsonProperty("sink")] public string? Sink { get; set; }

        /// <summary><c>hashed</c> (default: a stable per-seat hash) or <c>user</c> (user and machine names).</summary>
        [JsonProperty("identity")] public string? Identity { get; set; }

        /// <summary>Event kinds to send. Absent = <c>inventory</c> only; <c>command</c> and <c>ai</c>
        /// (per-seat usage) must be listed explicitly.</summary>
        [JsonProperty("events")] public List<string>? Events { get; set; }

        public bool Allows(string kind)
        {
            if (string.IsNullOrWhiteSpace(Sink)) return false;
            List<string> events = Events is { Count: > 0 } ? Events : new List<string> { "inventory" };
            return events.Any(e => string.Equals(e, kind, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Fleet telemetry to the company's own sink — never to the plugin's author (design §10). Off
    /// unless the policy names a sink; then buffered, sent in the background, dropped when the sink
    /// is down, and never carrying model data: events are built from names, versions, durations and
    /// outcomes only. Every emit is cheap and non-throwing; the worker does the I/O.
    /// </summary>
    internal static class TelemetryHub
    {
        private const int RecentCapacity = 50;
        private static readonly ConcurrentQueue<string> Pending = new();
        private static readonly ConcurrentQueue<string> Recent = new();
        private static readonly object WorkerGate = new();
        private static Timer? _worker;
        private static string? _lastError;
        private static readonly HttpClient Http = new(new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials,
        })
        { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>The active configuration, or null when telemetry is off.</summary>
        public static TelemetryPolicy? Configuration
        {
            get
            {
                try
                {
                    JObject? raw = PolicyStore.Current.IsPresent ? PolicyStore.Current.Document.Telemetry : null;
                    TelemetryPolicy? policy = raw?.ToObject<TelemetryPolicy>();
                    return policy is not null && !string.IsNullOrWhiteSpace(policy.Sink) ? policy : null;
                }
                catch { return null; }
            }
        }

        public static bool IsEnabled(string kind) => Configuration?.Allows(kind) == true;

        /// <summary>Queues one event when the policy allows its kind. Properties are copied as-is —
        /// callers pass names, numbers and outcomes, never payloads.</summary>
        public static void Emit(string kind, IReadOnlyDictionary<string, object?> properties)
        {
            try
            {
                TelemetryPolicy? policy = Configuration;
                if (policy is null || !policy.Allows(kind)) return;

                string line = BuildLine(kind, properties, policy, DateTimeOffset.UtcNow);
                Pending.Enqueue(line);
                Recent.Enqueue(line);
                while (Recent.Count > RecentCapacity && Recent.TryDequeue(out _)) { }
                EnsureWorker();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Telemetry emit failed (ignored)");
            }
        }

        /// <summary>Pure: one JSON line for an event. Identity is a stable per-seat hash unless the policy
        /// says <c>user</c>. The plugin and Revit versions ride along on every line.</summary>
        internal static string BuildLine(string kind, IReadOnlyDictionary<string, object?> properties,
            TelemetryPolicy policy, DateTimeOffset at, string? user = null, string? machine = null)
        {
            user ??= Environment.UserName;
            machine ??= Environment.MachineName;
            JObject line = new()
            {
                ["@t"] = at.ToString("o"),
                ["event"] = kind,
                ["plugin"] = SharedData.ToolData.PLUGIN_VERSION,
                ["revit"] = Bootstrap.CoreServices.RevitVersion,
            };
            if (string.Equals(policy.Identity, "user", StringComparison.OrdinalIgnoreCase))
            {
                line["user"] = user;
                line["machine"] = machine;
            }
            else
                line["seat"] = SeatHash(user, machine);

            foreach ((string key, object? value) in properties)
                line[key] = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            return line.ToString(Formatting.None);
        }

        internal static string SeatHash(string user, string machine) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machine.ToLowerInvariant() + "\\" + user.ToLowerInvariant())))[..16];

        /// <summary>What left this seat lately, newest last — the Organization panel's "Show recent events".</summary>
        public static IReadOnlyList<string> RecentEvents() => Recent.ToArray();

        public static (int Pending, string? LastError) Status() => (Pending.Count, _lastError);

        private static void EnsureWorker()
        {
            lock (WorkerGate)
                _worker ??= new Timer(_ => Flush(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        /// <summary>Sends everything queued. Called by the worker; also on demand (shutdown, tests).</summary>
        public static void Flush()
        {
            if (Pending.IsEmpty) return;
            List<string> batch = new();
            while (batch.Count < 500 && Pending.TryDequeue(out string? line)) batch.Add(line);
            if (batch.Count == 0) return;

            TelemetryPolicy? policy = Configuration;
            if (policy?.Sink is null) return; // switched off meanwhile: drop

            try
            {
                if (policy.Sink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    PostAsync(policy.Sink, batch).GetAwaiter().GetResult();
                else
                    WriteLines(policy.Sink, batch, DateTimeOffset.Now);
                _lastError = null;
            }
            catch (Exception ex)
            {
                // Dropped, not retried: a sink that is down must not grow a queue in Revit's memory.
                _lastError = ex.Message;
                Log.Debug(ex, "Telemetry batch dropped");
            }
        }

        /// <summary>Pure-ish: appends JSON lines to <c>&lt;folder&gt;\&lt;seat&gt;-&lt;date&gt;.jsonl</c>.</summary>
        internal static string WriteLines(string folder, IEnumerable<string> lines, DateTimeOffset day)
        {
            string dir = PolicySourceReader.ResolvePath(folder);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"{SeatHash(Environment.UserName, Environment.MachineName)}-{day:yyyy-MM-dd}.jsonl");
            File.AppendAllLines(file, lines);
            return file;
        }

        private static async Task PostAsync(string url, List<string> lines)
        {
            using StringContent content = new(string.Join("\n", lines) + "\n", Encoding.UTF8, "application/x-ndjson");
            using HttpResponseMessage response = await Http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>The events the host itself emits — one builder per kind, so the fields are fixed
    /// and reviewable, and none of them can carry a payload by accident.</summary>
    internal static class TelemetryEvents
    {
        public static void Command(string command, string source, long durationMs, string outcome)
        {
            if (!TelemetryHub.IsEnabled("command")) return;
            TelemetryHub.Emit("command", new Dictionary<string, object?>
            {
                ["command"] = command,
                ["source"] = source,
                ["durationMs"] = durationMs,
                ["outcome"] = outcome,
            });
        }

        public static void Inventory(IEnumerable<(string Id, string Version, string Zone, bool Enabled)> extensions,
            bool joined, string? organization, bool machinePolicy)
        {
            if (!TelemetryHub.IsEnabled("inventory")) return;
            TelemetryHub.Emit("inventory", new Dictionary<string, object?>
            {
                ["joined"] = joined,
                ["organization"] = organization,
                ["machinePolicy"] = machinePolicy,
                ["extensions"] = extensions.Select(e => new { e.Id, e.Version, e.Zone, e.Enabled }).ToList(),
            });
        }
    }
}
