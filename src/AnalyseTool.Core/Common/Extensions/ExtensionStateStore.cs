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
        private static string? _disabledFrom;     // the file the cache was read from: the profile can move (tests)

        private static JsonFileStore<State> Store => JsonFileStore<State>.For(Path.Combine(PathProvider.ProfilePath, "extensions-state.json"));

        // Under the lock: SetEnabled changes the same set, and a HashSet read during a write can throw.
        public static bool IsEnabled(string extensionId)
        {
            lock (Gate)
            {
                return !Disabled().Contains(extensionId);
            }
        }

        /// <summary>Enables/disables an extension. Takes effect on the next extension reload.</summary>
        public static void SetEnabled(string extensionId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(extensionId)) return;

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
                JsonFileStore<State> store = Store;
                if (_disabled is not null && _disabledFrom == store.Path) return _disabled;

                // Unreadable state = everything enabled (logged by the store); rewritten on the next toggle.
                _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _disabledFrom = store.Path;
                foreach (string id in store.Read().Disabled)
                    if (!string.IsNullOrWhiteSpace(id))
                        _disabled.Add(id);
                return _disabled;
            }
        }

        // Best-effort: if the write fails (logged), the in-memory set still applies for this session.
        private static void Save(HashSet<string> disabled) =>
            Store.Write(new State { Disabled = disabled.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList() });

        private sealed class State
        {
            [JsonProperty("disabled")]
            public List<string> Disabled { get; set; } = new();
        }
    }
}
