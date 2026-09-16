using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// Per-extension user state, persisted HOST-SIDE in <c>%LOCALAPPDATA%\&lt;plugin&gt;\extensions-state.json</c>
    /// — never in the vendor's <c>plugin.json</c> (a package file must not carry user state, or it would
    /// be lost on update and shipped on promote/publish).
    ///
    /// Only the DISABLED set is stored, so the default for anything unknown — including a freshly
    /// installed or newly created extension — is enabled. Applies to both zones (managed and dev):
    /// a disabled extension is skipped by the loader and gets no ribbon button, but stays listed
    /// in Settings.
    /// </summary>
    internal static class ExtensionStateStore
    {
        private static readonly object Gate = new();
        private static HashSet<string>? _disabled; // lazy cache; ids compared case-insensitively

        private static string StateFile => Path.Combine(PathProvider.ProfilePath, "extensions-state.json");

        /// <summary>A required extension (organization policy) is always enabled — including one the
        /// user disabled before the policy arrived; its stored flag is simply overruled, not rewritten,
        /// so leaving the organization gives the user's choice back.</summary>
        public static bool IsEnabled(string extensionId) =>
            RequiredExtensions.IsRequired(extensionId) || !Disabled().Contains(extensionId);

        /// <summary>Enables/disables an extension. Takes effect on the next extension reload.
        /// Throws when the organization policy requires the extension and the caller disables it.</summary>
        public static void SetEnabled(string extensionId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(extensionId)) return;
            if (!enabled && RequiredExtensions.IsRequired(extensionId))
                throw new InvalidOperationException(RequiredExtensions.RefusalFor(extensionId, "disabled"));

            lock (Gate)
            {
                HashSet<string> disabled = Disabled();
                bool changed = enabled ? disabled.Remove(extensionId) : disabled.Add(extensionId);
                if (changed) Save(disabled);
            }
        }

        private static HashSet<string> Disabled()
        {
            lock (Gate)
            {
                if (_disabled is not null) return _disabled;

                _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(StateFile))
                    {
                        State? state = JsonConvert.DeserializeObject<State>(File.ReadAllText(StateFile));
                        foreach (string id in state?.Disabled ?? new())
                            if (!string.IsNullOrWhiteSpace(id))
                                _disabled.Add(id);
                    }
                }
                catch { /* unreadable state = everything enabled; the file is rewritten on next toggle */ }
                return _disabled;
            }
        }

        private static void Save(HashSet<string> disabled)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(
                    new State { Disabled = disabled.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList() },
                    Formatting.Indented));
            }
            catch { /* best-effort; the in-memory set still applies for this session */ }
        }

        private sealed class State
        {
            [JsonProperty("disabled")]
            public List<string> Disabled { get; set; } = new();
        }
    }
}
