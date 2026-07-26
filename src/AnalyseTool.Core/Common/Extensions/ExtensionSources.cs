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
    }

    /// <summary>One extension source root with its zone semantics.</summary>
    internal sealed record ExtensionSourceRoot(string Path, ExtensionZone Zone, bool IsDefault);

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

        /// <summary>All roots with zone info: managed default, dev default, then user-added dev roots (deduped).</summary>
        public static IReadOnlyList<ExtensionSourceRoot> AllRoots()
        {
            List<ExtensionSourceRoot> roots = new()
            {
                new ExtensionSourceRoot(DefaultManagedRoot, ExtensionZone.Managed, IsDefault: true),
                new ExtensionSourceRoot(DefaultDevRoot, ExtensionZone.Dev, IsDefault: true),
            };
            foreach (string p in LoadUserRoots())
                if (!roots.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase)))
                    roots.Add(new ExtensionSourceRoot(p, ExtensionZone.Dev, IsDefault: false));
            return roots;
        }

        /// <summary>All root paths (both zones) — for callers that only validate/display paths.</summary>
        public static IReadOnlyList<string> Roots() => AllRoots().Select(r => r.Path).ToList();

        /// <summary>User-added roots only (excludes the built-in defaults).</summary>
        public static IReadOnlyList<string> UserRoots() => LoadUserRoots();

        /// <summary>The legacy per-version directory under the dev root (<c>extensions\&lt;year&gt;</c>) —
        /// still shown in Settings for users whose extensions live in the deprecated layout.</summary>
        public static string DefaultVersionDir(string revitVersion) =>
            Path.Combine(DefaultDevRoot, revitVersion);

        /// <summary>Adds a user dev root (no-op for the built-in roots or duplicates). Returns the normalized path.</summary>
        public static string AddRoot(string path)
        {
            string full = Path.GetFullPath(path.Trim());
            if (string.Equals(full, DefaultManagedRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, DefaultDevRoot, StringComparison.OrdinalIgnoreCase))
                return full; // built-ins are always implicit

            List<string> roots = LoadUserRoots().ToList();
            if (!roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(full);
                SaveUserRoots(roots);
            }
            return full;
        }

        /// <summary>Removes a user root (the built-in roots cannot be removed).</summary>
        public static void RemoveRoot(string path)
        {
            string full = Path.GetFullPath(path.Trim());
            List<string> roots = LoadUserRoots()
                .Where(r => !string.Equals(r, full, StringComparison.OrdinalIgnoreCase))
                .ToList();
            SaveUserRoots(roots);
        }

        private static List<string> LoadUserRoots()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsFile))?.Paths ?? new();
            }
            catch { /* fall through to empty */ }
            return new();
        }

        private static void SaveUserRoots(List<string> paths)
        {
            try
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(new Settings { Paths = paths }, Formatting.Indented));
            }
            catch { /* best-effort; non-fatal */ }
        }

        private sealed class Settings
        {
            public List<string> Paths { get; set; } = new();
        }
    }
}
