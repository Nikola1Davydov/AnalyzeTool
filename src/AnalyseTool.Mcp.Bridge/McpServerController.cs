using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Dispatch;
using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace AnalyseTool.Mcp.Bridge
{
    /// <summary>
    /// Owns the single <see cref="McpBridgeServer"/> instance plus its persisted on/off + port
    /// settings (mcp.json under the profile folder). Started from <c>AnalyseToolBootstrap.Initialize</c>;
    /// the Settings page reads/writes it through the GetMcpStatus / SetMcpServer built-in commands.
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

            if (_settings.Enabled)
                TryStart();
        }

        /// <summary>
        /// The shared secret every bridge request must carry. Created once and kept in mcp.json, which
        /// lives under the user's own profile folder — the same trust level as the AI client config that
        /// will hold a copy of it.
        /// </summary>
        private static void EnsureToken()
        {
            if (!string.IsNullOrWhiteSpace(_settings.Token)) return;

            _settings.Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            if (!Save(_settings))
                Log.Error("MCP bridge token could not be saved: it lives in memory only, and an AI client " +
                          "configured with it stops working when Revit restarts");
        }

        /// <summary>Applies a new on/off + port from the Settings page, persists it, and starts/stops live.</summary>
        public static object Apply(bool enabled, int? port)
        {
            int previousPort = _settings.Port;
            _settings.Enabled = enabled;
            if (port is > 0 and <= 65535)
                _settings.Port = port.Value;

            Save(_settings);

            // Stopping cancels every running agent command (their tokens hang off the listener's), so
            // a Save on the Settings page that changed nothing must not restart the bridge.
            if (NeedsRestart(enabled, _bridge?.IsRunning ?? false, previousPort, _settings.Port))
            {
                _bridge?.Stop();
                if (enabled)
                    TryStart();
            }

            return Status();
        }

        /// <summary>Whether applying settings has to stop (and, when enabled, start) the bridge: when
        /// it should be off but runs, should run but does not, or must move to another port.</summary>
        internal static bool NeedsRestart(bool enabled, bool running, int currentPort, int requestedPort) =>
            enabled != running || (enabled && currentPort != requestedPort);

        public static object Status() => new
        {
            running = _bridge?.IsRunning ?? false,
            enabled = _settings.Enabled,
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Defaults mean a NEW token, so every configured AI client stops authenticating — worth
                // a line in the log for whoever wonders why.
                Log.Warning(ex, "mcp.json could not be read; MCP settings fall back to defaults with a new token");
            }
            return new();
        }

        /// <summary>Best-effort write; false when it failed (logged).</summary>
        private static bool Save(McpSettings settings)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "mcp.json could not be written");
                return false;
            }
        }

        private sealed class McpSettings
        {
            public bool Enabled { get; set; }
            public int Port { get; set; } = DefaultPort;

            /// <summary>Shared secret for the bridge; generated on first run (see EnsureToken).</summary>
            public string Token { get; set; } = string.Empty;
        }
    }
}
