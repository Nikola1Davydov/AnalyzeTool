using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>Which zone an extension source root belongs to — the semantics differ, the on-disk
    /// layout parsing does not (see <see cref="ExtensionCatalog"/>).</summary>
    internal enum ExtensionZone
    {
        /// <summary>Owned by the Extension Manager: packages are installed / removed / updated here.</summary>
        Managed,

        /// <summary>Authored by the user: loose folders, live Reload, no install/update semantics.</summary>
        Dev,

        /// <summary>Pre-installed by an administrator under <c>%ProgramData%</c>: package layout, loaded
        /// like managed, but read-only for the Extension Manager (no install / remove / update).</summary>
        Machine,
    }

    /// <summary>One extension source root with its zone semantics. <paramref name="FromPolicy"/> marks a
    /// root the organization policy added: listed, scanned, never removable from the UI.</summary>
    internal sealed record ExtensionSourceRoot(string Path, ExtensionZone Zone, bool IsDefault, bool FromPolicy = false);

    /// <summary>
    /// Resolves the roots that are scanned for extensions. An extension is a folder with a
    /// <c>plugin.json</c> directly under a root (<c>&lt;root&gt;\&lt;id&gt;</c>); per-Revit-year binaries live in
    /// optional year subfolders inside it (<c>&lt;id&gt;\2025\...</c>). The legacy layout
    /// <c>&lt;root&gt;\&lt;revitYear&gt;\&lt;id&gt;</c> is still recognized (deprecated).
    ///
    /// Two zones:
    /// <list type="bullet">
    /// <item><see cref="DefaultManagedRoot"/> (<c>extensions-dist</c>) — the MANAGED zone, owned by the
    /// Extension Manager. A NEW folder on purpose: clean invariants, and the historical
    /// <c>extensions</c> folder keeps its behavior exactly.</item>
    /// <item><see cref="DefaultDevRoot"/> (<c>extensions</c>) plus any user-added roots — the DEV zone
    /// for hand-authored extensions. User roots are persisted in <c>extensions.json</c> under the
    /// profile folder.</item>
    /// </list>
    /// </summary>
    internal static class ExtensionSources
    {
        /// <summary>Built-in managed root under the user profile (installed packages).</summary>
        public static string DefaultManagedRoot => PathProvider.ExtensionsDistRoot;

        /// <summary>Built-in dev root under the user profile — the historical <c>extensions</c> folder
        /// (templates are scaffolded here; the legacy per-year layout also lives here).</summary>
        public static string DefaultDevRoot => PathProvider.ExtensionsRoot;

        private static string SettingsFile => Path.Combine(PathProvider.ProfilePath, "extensions.json");

        /// <summary>Machine-wide packages an administrator pre-installed (<c>%ProgramData%</c>). Read-only.</summary>
        public static string MachineManagedRoot => PathProvider.MachineExtensionsDistRoot;

        /// <summary>All roots with zone info, in load order (first folder to claim an id wins):
        /// the machine root, the managed default, the dev default, the roots the organization policy
        /// adds, then user-added dev roots (deduped).</summary>
        public static IReadOnlyList<ExtensionSourceRoot> AllRoots()
        {
            List<ExtensionSourceRoot> roots = new();
            // Only listed when an administrator actually created it: a single-seat install must not
            // see a "folder not found" row for a folder it has no business with.
            if (Directory.Exists(MachineManagedRoot))
                roots.Add(new ExtensionSourceRoot(MachineManagedRoot, ExtensionZone.Machine, IsDefault: true));
            roots.Add(new ExtensionSourceRoot(DefaultManagedRoot, ExtensionZone.Managed, IsDefault: true));
            roots.Add(new ExtensionSourceRoot(DefaultDevRoot, ExtensionZone.Dev, IsDefault: true));
            foreach (string p in PolicyRoots())
                if (!roots.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase)))
                    roots.Add(new ExtensionSourceRoot(p, ExtensionZone.Dev, IsDefault: true, FromPolicy: true));
            foreach (string p in LoadUserRoots())
                if (!roots.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase)))
                    roots.Add(new ExtensionSourceRoot(p, ExtensionZone.Dev, IsDefault: false));
            return roots;
        }

        /// <summary>Roots the organization policy declares (<c>extensions.roots</c>), with <c>%ENV%</c>
        /// expanded so one policy line serves every user. Meant for UNC shares and local folders: a root
        /// is a LOAD root, and a synced OneDrive / SharePoint folder must never be one (the sync client
        /// and Revit would fight over the DLLs) — such a root is still added, but logged as a warning.
        /// Malformed entries are skipped, not fatal.</summary>
        public static IReadOnlyList<string> PolicyRoots()
        {
            List<string>? declared = PolicyStore.Current.Document.Extensions?.Roots;
            if (declared is null || !PolicyStore.Current.IsPresent) return Array.Empty<string>();

            List<string> roots = new();
            foreach (string raw in declared)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim()));
                    if (LooksSynced(full))
                        Serilog.Log.Warning(
                            "Policy extension root {Root} looks like a synced OneDrive/SharePoint folder. " +
                            "Extensions are loaded in place from a root; distribute SharePoint content " +
                            "through a feed instead.", full);
                    roots.Add(full);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Ignoring malformed policy extension root {Root}", raw);
                }
            }
            return roots;
        }

        /// <summary>A path the OneDrive client is likely syncing: under one of its known roots, or a
        /// SharePoint library synced into the profile (<c>&lt;tenant&gt;\&lt;site&gt; - Documents</c>).</summary>
        internal static bool LooksSynced(string fullPath)
        {
            foreach (string var in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                string? root = Environment.GetEnvironmentVariable(var);
                if (!string.IsNullOrWhiteSpace(root) &&
                    fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return fullPath.Contains(" - Documents" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.EndsWith(" - Documents", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The user may not add or remove their own roots while the policy locks <c>extensions.roots</c>.</summary>
        public static bool RootsLocked => PolicyStore.Current.IsLocked(PolicySettings.ExtensionsRoots);

        /// <summary>All root paths (both zones) — for callers that only validate/display paths.</summary>
        public static IReadOnlyList<string> Roots() => AllRoots().Select(r => r.Path).ToList();

        /// <summary>User-added roots only (excludes the built-in defaults).</summary>
        public static IReadOnlyList<string> UserRoots() => LoadUserRoots();

        /// <summary>The legacy per-version directory under the dev root (<c>extensions\&lt;year&gt;</c>) —
        /// still shown in Settings for users whose extensions live in the deprecated layout.</summary>
        public static string DefaultVersionDir(string revitVersion) =>
            Path.Combine(DefaultDevRoot, revitVersion);

        /// <summary>
        /// Where the authoring commands (SaveAsCommand, SaveExtensionUi) save what they generate when
        /// the caller names no root. Defaults to the built-in dev root.
        ///
        /// Falls back to that default whenever the stored choice is no longer a registered DEV root:
        /// a folder the user has since removed must not keep swallowing generated scripts where
        /// nothing scans for them.
        /// </summary>
        public static string AuthoringRoot
        {
            get
            {
                string? stored = Load().AuthoringRoot;
                if (string.IsNullOrWhiteSpace(stored)) return DefaultDevRoot;

                return AllRoots().Any(r => r.Zone == ExtensionZone.Dev &&
                                           string.Equals(r.Path, stored, StringComparison.OrdinalIgnoreCase))
                    ? stored!
                    : DefaultDevRoot;
            }
        }

        /// <summary>Chooses where generated scripts land. Null on success, otherwise why not.
        /// Managed roots are refused: the Extension Manager owns <c>extensions-dist</c>, and the next
        /// install or update there would overwrite whatever was generated into it.</summary>
        public static string? SetAuthoringRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "A folder is required.";

            string full = Path.GetFullPath(path.Trim());
            ExtensionSourceRoot? root = AllRoots()
                .FirstOrDefault(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase));

            if (root is null)
                return $"'{full}' is not a registered extension source. Add it as a path first.";
            if (root.Zone != ExtensionZone.Dev)
                return "Installed packages are managed by the Extension Manager — an update would " +
                       "overwrite anything generated there. Choose one of your own folders.";

            Settings settings = Load();
            settings.AuthoringRoot = full;
            Save(settings);
            return null;
        }

        /// <summary>Adds a user dev root (no-op for the built-in roots or duplicates). Returns the normalized path.
        /// Throws when the organization policy locks the root list.</summary>
        public static string AddRoot(string path)
        {
            if (RootsLocked)
                throw new InvalidOperationException(PolicyStore.Current.LockedMessage(PolicySettings.ExtensionsRoots));

            string full = Path.GetFullPath(path.Trim());
            if (string.Equals(full, MachineManagedRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, DefaultManagedRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, DefaultDevRoot, StringComparison.OrdinalIgnoreCase)
                || PolicyRoots().Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
                return full; // built-ins and policy roots are always implicit

            List<string> roots = LoadUserRoots().ToList();
            if (!roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(full);
                SaveUserRoots(roots);
            }
            return full;
        }

        /// <summary>Removes a user root. The built-in roots and the roots the policy declares cannot be
        /// removed; a locked root list refuses every removal.</summary>
        public static void RemoveRoot(string path)
        {
            if (RootsLocked)
                throw new InvalidOperationException(PolicyStore.Current.LockedMessage(PolicySettings.ExtensionsRoots));

            string full = Path.GetFullPath(path.Trim());
            if (PolicyRoots().Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"'{full}' is declared by your organization's policy ({PolicyStore.Current.Path}) and cannot be removed here.");

            List<string> roots = LoadUserRoots()
                .Where(r => !string.Equals(r, full, StringComparison.OrdinalIgnoreCase))
                .ToList();
            SaveUserRoots(roots);
        }

        private static List<string> LoadUserRoots() => Load().Paths;

        private static void SaveUserRoots(List<string> paths)
        {
            // Read-modify-write, not a fresh object: the same file carries the authoring root, and a
            // from-scratch rewrite would reset it every time a path is added or removed.
            Settings settings = Load();
            settings.Paths = paths;
            Save(settings);
        }

        private static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsFile)) ?? new();
            }
            catch { /* fall through to defaults */ }
            return new();
        }

        private static void Save(Settings settings)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch { /* best-effort; non-fatal */ }
        }

        private sealed class Settings
        {
            public List<string> Paths { get; set; } = new();

            /// <summary>Absent in every file written before this setting existed — which is exactly
            /// what "no choice made, use the built-in dev root" looks like.</summary>
            public string? AuthoringRoot { get; set; }
        }
    }
}
