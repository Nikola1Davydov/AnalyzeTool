using AnalyseTool.Core.Common;
using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.App.Common.Extensions
{
    /// <summary>
    /// Visibility of the HOST's own ribbon buttons (AnalyseTool / Family Manager / Component),
    /// persisted in <c>%LOCALAPPDATA%\&lt;plugin&gt;\ribbon-state.json</c>. Hiding a button is UI-only:
    /// the commands behind it stay registered and callable (MCP, AT.invoke, dock pane). Only the
    /// HIDDEN set is stored, so everything unknown defaults to visible. The Manage stack
    /// (Settings / Reload / Report a bug) is deliberately not togglable — the user must always
    /// keep a way back into Settings.
    /// </summary>
    internal static class HostButtonState
    {
        private static readonly object Gate = new();
        private static HashSet<string>? _hidden;

        private static string StateFile => Path.Combine(PathProvider.ProfilePath, "ribbon-state.json");

        public static bool IsVisible(string buttonKey) => !Hidden().Contains(buttonKey);

        /// <summary>Persists the flag; the ribbon itself is updated by the caller (UI thread).</summary>
        public static void SetVisible(string buttonKey, bool visible)
        {
            if (string.IsNullOrWhiteSpace(buttonKey)) return;

            lock (Gate)
            {
                HashSet<string> hidden = Hidden();
                bool changed = visible ? hidden.Remove(buttonKey) : hidden.Add(buttonKey);
                if (changed) Save(hidden);
            }
        }

        private static HashSet<string> Hidden()
        {
            lock (Gate)
            {
                if (_hidden is not null) return _hidden;

                _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(StateFile))
                    {
                        State? state = JsonConvert.DeserializeObject<State>(File.ReadAllText(StateFile));
                        foreach (string key in state?.HiddenButtons ?? new())
                            if (!string.IsNullOrWhiteSpace(key))
                                _hidden.Add(key);
                    }
                }
                catch { /* unreadable state = everything visible; rewritten on next toggle */ }
                return _hidden;
            }
        }

        private static void Save(HashSet<string> hidden)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(
                    new State { HiddenButtons = hidden.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList() },
                    Formatting.Indented));
            }
            catch { /* best-effort; the in-memory set still applies for this session */ }
        }

        private sealed class State
        {
            [JsonProperty("hiddenButtons")]
            public List<string> HiddenButtons { get; set; } = new();
        }
    }
}
