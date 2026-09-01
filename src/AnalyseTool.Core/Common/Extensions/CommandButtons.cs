using Newtonsoft.Json;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>A command the user put on the ribbon, with what the button should read.</summary>
    internal sealed record CommandButtonPin(string Command, string Label, string? Tooltip);

    /// <summary>
    /// The USER's say in which commands get a ribbon button, persisted in <c>command-buttons.json</c>
    /// under the profile folder.
    ///
    /// A manifest already answers that question — <c>ui.button.command</c> is the author's default —
    /// but it is the wrong place for a user's preference twice over. It cannot be written for an
    /// installed package (the Extension Manager owns those folders, and an update would put the old
    /// answer back), and it does not exist at all for a built-in command. So this store holds
    /// OVERRIDES on top of the manifest rather than a copy of the state: <c>true</c> means the user
    /// asked for a button, <c>false</c> that they took one away, and no entry means the manifest
    /// decides. Writing an override that merely agrees with the manifest is pointless and harmful —
    /// an extension that later changes its own button would be outvoted by a preference nobody
    /// remembers setting — so callers clear the override instead (see <c>SetCommandButton</c>).
    ///
    /// The pins carry their own label and tooltip because the ribbon is built at Revit startup, long
    /// before any command is registered: the buttons have to be drawable from this file alone.
    /// </summary>
    internal static class CommandButtons
    {
        private static readonly object Gate = new();
        private static State? _state;

        private static string StateFile => Path.Combine(PathProvider.ProfilePath, "command-buttons.json");

        /// <summary>True when the user asked for a ribbon button, false when they took one away, null
        /// when they never said and the manifest decides.</summary>
        public static bool? Override(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            lock (Gate)
            {
                State state = Load();
                if (state.Hidden.Any(h => Same(h, command))) return false;
                return state.Pinned.Any(p => Same(p.Command, command)) ? (bool?)true : null;
            }
        }

        /// <summary>The pinned commands, in the order they were pinned — what the ribbon renders.</summary>
        public static IReadOnlyList<CommandButtonPin> Pinned()
        {
            lock (Gate)
                return Load().Pinned
                    .Where(p => !string.IsNullOrWhiteSpace(p.Command))
                    .Select(p => new CommandButtonPin(p.Command, Label(p.Command, p.Label), p.Tooltip))
                    .ToList();
        }

        /// <summary>Records the user's choice: true pins, false suppresses, null forgets the override
        /// and lets the manifest decide again.</summary>
        public static void Set(string command, bool? onRibbon, string? label = null, string? tooltip = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            lock (Gate)
            {
                State state = Load();
                state.Pinned.RemoveAll(p => Same(p.Command, command));
                state.Hidden.RemoveAll(h => Same(h, command));

                if (onRibbon == true)
                    state.Pinned.Add(new Pin { Command = command.Trim(), Label = label, Tooltip = tooltip });
                else if (onRibbon == false)
                    state.Hidden.Add(command.Trim());

                Save(state);
            }
        }

        /// <summary>The commands an extension manifest already puts on the ribbon — the author's
        /// defaults. Read once per listing: every caller needs the whole set, and it costs a scan of
        /// every manifest on disk.</summary>
        public static HashSet<string> ManifestDeclared(string revitVersion) =>
            ManifestDeclared(ExtensionCatalog.EnumerateAll(revitVersion));

        /// <summary>The same, from a scan the caller already has — a listing that needs more than one
        /// fact per extension should not walk every manifest on disk once per fact.</summary>
        public static HashSet<string> ManifestDeclared(IEnumerable<ExtensionDescriptor> found)
        {
            HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);
            foreach (ExtensionDescriptor descriptor in found)
            {
                // Any manifest button may name a command, so all of them are checked — with ui.buttons
                // the command could sit on the second entry as easily as the first.
                string? command = descriptor.Manifest.Ui?.EffectiveButtons()
                    .Select(b => b.Command)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                if (!string.IsNullOrWhiteSpace(command)) declared.Add(command!.Trim());
            }
            return declared;
        }

        /// <summary>What a button for this command reads. A stored label wins; otherwise the command's
        /// own name is all there is — <c>"niko.sheets.RenameSheets"</c> becomes <c>"Rename Sheets"</c>,
        /// because a ribbon is not the place to read a dotted identifier.</summary>
        public static string Label(string command, string? stored = null)
        {
            if (!string.IsNullOrWhiteSpace(stored)) return stored!.Trim();

            string tail = command.Substring(command.LastIndexOf('.') + 1);
            return Regex.Replace(tail, "(?<=[a-z0-9])(?=[A-Z])", " ");
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static State Load()
        {
            if (_state is not null) return _state;

            try
            {
                if (File.Exists(StateFile))
                    _state = JsonConvert.DeserializeObject<State>(File.ReadAllText(StateFile));
            }
            catch { /* unreadable state = no overrides; rewritten on the next toggle */ }

            return _state ??= new State();
        }

        private static void Save(State state)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch { /* best-effort; the in-memory state still applies for this session */ }
        }

        private sealed class State
        {
            [JsonProperty("pinned")]
            public List<Pin> Pinned { get; set; } = new();

            [JsonProperty("hidden")]
            public List<string> Hidden { get; set; } = new();
        }

        private sealed class Pin
        {
            [JsonProperty("command")]
            public string Command { get; set; } = string.Empty;

            [JsonProperty("label")]
            public string? Label { get; set; }

            [JsonProperty("tooltip")]
            public string? Tooltip { get; set; }
        }
    }
}
