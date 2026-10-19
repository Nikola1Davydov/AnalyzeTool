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
        private static readonly JsonFileStore<Settings> Store = new(Path.Combine(PathProvider.ProfilePath, "codeexec.json"));
        private static readonly object Gate = new();
        private static bool? _enabled;

        public static bool Enabled
        {
            get
            {
                lock (Gate)
                {
                    _enabled ??= Store.Read().Enabled;
                    return _enabled.Value;
                }
            }
        }

        public static void SetEnabled(bool value)
        {
            lock (Gate)
            {
                _enabled = value;
                // Not persisted (logged by the store): the switch still applies for this session.
                Store.Write(new Settings { Enabled = value });
            }
        }

        private sealed class Settings
        {
            public bool Enabled { get; set; }
        }
    }
}
