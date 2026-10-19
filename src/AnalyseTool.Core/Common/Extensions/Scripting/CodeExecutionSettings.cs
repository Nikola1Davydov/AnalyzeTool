using System.IO;

namespace AnalyseTool.Core.Common.Extensions.Scripting
{
    /// <summary>
    /// Persisted on/off switch for ad-hoc C# code execution (the <c>ExecuteRevitCode</c> command).
    /// OFF by default: running arbitrary AI/user code in-process is full-trust, so it must be opted
    /// into explicitly. Persisted to <c>codeexec.json</c> under the profile folder.
    /// </summary>
    internal static class CodeExecutionSettings
    {
        private static JsonFileStore<Settings> Store => JsonFileStore<Settings>.For(Path.Combine(PathProvider.ProfilePath, "codeexec.json"));
        private static readonly object Gate = new();
        private static bool? _enabled;
        private static string? _enabledFrom; // the file _enabled was read from: the profile can move (tests)

        public static bool Enabled
        {
            get
            {
                lock (Gate)
                {
                    JsonFileStore<Settings> store = Store;
                    if (_enabled is null || _enabledFrom != store.Path)
                    {
                        _enabled = store.Read().Enabled;
                        _enabledFrom = store.Path;
                    }
                    return _enabled.Value;
                }
            }
        }

        public static void SetEnabled(bool value)
        {
            lock (Gate)
            {
                JsonFileStore<Settings> store = Store;
                _enabled = value;
                _enabledFrom = store.Path;
                // Not persisted (logged by the store): the switch still applies for this session.
                store.Write(new Settings { Enabled = value });
            }
        }

        private sealed class Settings
        {
            public bool Enabled { get; set; }
        }
    }
}
