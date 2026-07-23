using Newtonsoft.Json;
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
        private static bool? _enabled;

        private static string SettingsFile => Path.Combine(PathProvider.ProfilePath, "codeexec.json");

        public static bool Enabled
        {
            get
            {
                _enabled ??= Load();
                return _enabled.Value;
            }
        }

        public static void SetEnabled(bool value)
        {
            _enabled = value;
            Save(value);
        }

        private static bool Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsFile))?.Enabled ?? false;
            }
            catch { /* fall through to the safe default */ }
            return false;
        }

        private static void Save(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(new Settings { Enabled = enabled }, Formatting.Indented));
            }
            catch { /* best-effort; non-fatal */ }
        }

        private sealed class Settings
        {
            public bool Enabled { get; set; }
        }
    }
}
