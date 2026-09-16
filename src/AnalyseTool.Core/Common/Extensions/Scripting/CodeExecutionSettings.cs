using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.Core.Common.Extensions.Scripting
{
    /// <summary>
    /// Persisted on/off switch for ad-hoc C# code execution (the <c>ExecuteRevitCode</c> command).
    /// OFF by default: running arbitrary AI/user code in-process is full-trust, so it must be opted
    /// into explicitly. Persisted to <c>codeexec.json</c> under the profile folder.
    /// <para>
    /// An organization policy (<see cref="PolicyStore"/>) can supply the value: locked, it wins and
    /// <see cref="SetEnabled"/> refuses; unlocked, it is the default until the user chooses.
    /// </para>
    /// </summary>
    internal static class CodeExecutionSettings
    {
        private static bool? _userChoice;
        private static bool _userChoiceLoaded;

        private static string SettingsFile => Path.Combine(PathProvider.ProfilePath, "codeexec.json");

        public static bool Enabled =>
            PolicyStore.Current.Resolve(PolicySettings.CodeExecutionEnabled, PolicyValue, UserChoice, fallback: false);

        /// <summary>The policy owns this setting: the Settings page shows it read-only.</summary>
        public static bool IsManaged => PolicyStore.Current.IsLocked(PolicySettings.CodeExecutionEnabled);

        /// <summary>Which layer <see cref="Enabled"/> comes from: policy, user, policy-default or default.</summary>
        public static string Origin =>
            PolicyStore.Current.OriginOf(PolicySettings.CodeExecutionEnabled, PolicyValue, UserChoice);

        /// <summary>Persists the user's choice. Throws when the policy locks the setting — the caller
        /// surfaces the message; nothing is written.</summary>
        public static void SetEnabled(bool value)
        {
            if (IsManaged)
                throw new InvalidOperationException(PolicyStore.Current.LockedMessage(PolicySettings.CodeExecutionEnabled));

            _userChoice = value;
            _userChoiceLoaded = true;
            Save(value);
        }

        private static bool? PolicyValue => PolicyStore.Current.Document.CodeExecution?.Enabled;

        /// <summary>Null until the user has ever toggled the switch — that is what lets a policy default apply.</summary>
        private static bool? UserChoice
        {
            get
            {
                if (!_userChoiceLoaded)
                {
                    _userChoice = Load();
                    _userChoiceLoaded = true;
                }
                return _userChoice;
            }
        }

        private static bool? Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsFile))?.Enabled ?? false;
            }
            catch { /* unreadable = no choice made; the policy default or the safe default applies */ }
            return null;
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
