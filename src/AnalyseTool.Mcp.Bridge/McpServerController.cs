using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace AnalyseTool.Mcp.Bridge
{
    /// <summary>
    /// Owns the single <see cref="McpBridgeServer"/> instance plus its persisted on/off + port
    /// settings (mcp.json under the profile folder). Started from <c>AnalyseToolBootstrap.Initialize</c>;
    /// the Settings page reads/writes it through the GetMcpStatus / SetMcpServer built-in commands.
    /// An organization policy (<c>mcp.enabled</c>) can supply the on/off value: locked, it wins and
    /// <see cref="Apply"/> refuses; unlocked, it is the default until the user chooses.
    /// </summary>
    internal static class McpServerController
    {
        public const int DefaultPort = McpWire.DefaultPort;

        private static McpBridgeServer? _bridge;
        private static McpSettings _settings = new();
        private static string? _lastError;

        private static string SettingsFile =>
            Path.Combine(PathProvider.ProfilePath, "mcp.json");

        /// <summary>Path the MCP stdio server exe ships to (next to the plugin, in a "mcp" subfolder).</summary>
        public static string ServerExePath =>
            Path.Combine(PathProvider.RootDirectory, "mcp", "AnalyseTool.Mcp.exe");

        public static void Initialize(CommandQueue queue)
        {
            if (_bridge != null) return;

            _settings = Load();
            EnsureToken();
            _bridge = new McpBridgeServer(queue, _settings.Token);

            if (EffectiveEnabled)
                TryStart();
        }

        private static bool? PolicyEnabled => PolicyStore.Current.Document.Mcp?.Enabled;

        /// <summary>The policy locks the switch: Settings renders it read-only.</summary>
        public static bool IsManaged => PolicyStore.Current.IsLocked(PolicySettings.McpEnabled);

        /// <summary>Locked policy value → the user's choice → policy default → off.</summary>
        private static bool EffectiveEnabled =>
            PolicyStore.Current.Resolve(PolicySettings.McpEnabled, PolicyEnabled, _settings.Enabled, fallback: false);

        /// <summary>
        /// The shared secret every bridge request must carry. Created once and kept in mcp.json, which
        /// lives under the user's own profile folder — the same trust level as the AI client config that
        /// will hold a copy of it.
        /// </summary>
        private static void EnsureToken()
        {
            if (!string.IsNullOrWhiteSpace(_settings.Token)) return;

            _settings.Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            Save(_settings);
        }

        /// <summary>Applies a new on/off + port from the Settings page, persists it, and starts/stops live.
        /// Throws when the organization policy locks the on/off switch; nothing is written then.</summary>
        public static object Apply(bool enabled, int? port)
        {
            if (IsManaged && enabled != EffectiveEnabled)
                throw new InvalidOperationException(PolicyStore.Current.LockedMessage(PolicySettings.McpEnabled));

            _settings.Enabled = enabled;
            if (port is > 0 and <= 65535)
                _settings.Port = port.Value;

            Save(_settings);

            _bridge?.Stop();
            if (EffectiveEnabled)
                TryStart();

            return Status();
        }

        public static object Status() => new
        {
            running = _bridge?.IsRunning ?? false,
            enabled = EffectiveEnabled,
            // True when an organization policy locks the switch: render it read-only.
            managed = IsManaged,
            origin = PolicyStore.Current.OriginOf(PolicySettings.McpEnabled, PolicyEnabled, _settings.Enabled),
            port = (_bridge?.IsRunning ?? false) ? _bridge!.Port : _settings.Port,
            configuredPort = _settings.Port,
            wsUrl = $"ws://127.0.0.1:{((_bridge?.IsRunning ?? false) ? _bridge!.Port : _settings.Port)}/",
            serverExePath = ServerExePath,
            serverExeExists = File.Exists(ServerExePath),
            // For the generated AI-client config snippet. It is a local secret, not a credential the
            // frontend can leak anywhere: the WebView only renders it back into the snippet the user
            // copies into their own MCP client config.
            token = _settings.Token,
            lastError = _lastError,
        };

        private static void TryStart()
        {
            try
            {
                _lastError = null;
                _bridge!.Start(_settings.Port);
                Log.Information("MCP bridge started on port {Port}", _bridge!.Port);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message; // e.g. port already in use
                Log.Error(ex, "MCP bridge failed to start on port {Port}", _settings.Port);
            }
        }

        private static McpSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<McpSettings>(File.ReadAllText(SettingsFile)) ?? new();
            }
            catch { /* fall through to defaults */ }
            return new();
        }

        private static void Save(McpSettings settings)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch { /* best-effort; non-fatal */ }
        }

        private sealed class McpSettings
        {
            /// <summary>The user's own choice; null until they toggle the switch (the token write on
            /// first run leaves it null), which is what lets an unlocked policy value act as the
            /// default. Files from before this field was nullable carry an explicit false and count
            /// as a choice — a locked policy still wins over them.</summary>
            public bool? Enabled { get; set; }
            public int Port { get; set; } = DefaultPort;

            /// <summary>Shared secret for the bridge; generated on first run (see EnsureToken).</summary>
            public string Token { get; set; } = string.Empty;
        }
    }
}
