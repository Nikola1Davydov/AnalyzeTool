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

            _bridge = new McpBridgeServer(queue);
            _settings = Load();

            if (_settings.Enabled)
                TryStart();
        }

        /// <summary>Applies a new on/off + port from the Settings page, persists it, and starts/stops live.</summary>
        public static object Apply(bool enabled, int? port)
        {
            _settings.Enabled = enabled;
            if (port is > 0 and <= 65535)
                _settings.Port = port.Value;

            Save(_settings);

            _bridge?.Stop();
            if (enabled)
                TryStart();

            return Status();
        }

        public static object Status() => new
        {
            running = _bridge?.IsRunning ?? false,
            enabled = _settings.Enabled,
            port = (_bridge?.IsRunning ?? false) ? _bridge!.Port : _settings.Port,
            configuredPort = _settings.Port,
            wsUrl = $"ws://127.0.0.1:{((_bridge?.IsRunning ?? false) ? _bridge!.Port : _settings.Port)}/",
            serverExePath = ServerExePath,
            serverExeExists = File.Exists(ServerExePath),
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
            public bool Enabled { get; set; }
            public int Port { get; set; } = DefaultPort;
        }
    }
}
