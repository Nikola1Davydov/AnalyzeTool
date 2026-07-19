using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// Resolves the directories that are scanned for extensions. Extensions are organised as
    /// <c>&lt;root&gt;\&lt;revitVersion&gt;\&lt;extension&gt;</c> (e.g. <c>...\extensions\2025\acme.sample</c>), so a
    /// single machine can host builds for several Revit versions at once and we only ever load the
    /// folder matching the running host.
    ///
    /// The default root is <see cref="PathProvider.ExtensionsRoot"/>. Users can add extra roots from
    /// Settings; they are persisted in <c>extensions.json</c> under the profile folder and must follow
    /// the same per-version layout. An added root that does not yet contain the version folder is kept
    /// but simply contributes nothing until it does.
    /// </summary>
    internal static class ExtensionSources
    {
        /// <summary>Built-in root under the user profile.</summary>
        public static string DefaultRoot => PathProvider.ExtensionsRoot;

        private static string SettingsFile => Path.Combine(PathProvider.ProfilePath, "extensions.json");

        /// <summary>All roots to search: the default one first, then any user-added roots (deduped).</summary>
        public static IReadOnlyList<string> Roots()
        {
            List<string> roots = new() { DefaultRoot };
            foreach (string p in LoadUserRoots())
                if (!roots.Any(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase)))
                    roots.Add(p);
            return roots;
        }

        /// <summary>User-added roots only (excludes the default).</summary>
        public static IReadOnlyList<string> UserRoots() => LoadUserRoots();

        /// <summary>Version-scoped scan directories for the running Revit version (the year, e.g. "2025").</summary>
        public static IReadOnlyList<string> ScanDirs(string revitVersion) =>
            Roots().Select(root => Path.Combine(root, revitVersion)).ToList();

        /// <summary>The default root's version directory (where new templates are created and the
        /// "Open folder" button lands).</summary>
        public static string DefaultVersionDir(string revitVersion) =>
            Path.Combine(DefaultRoot, revitVersion);

        /// <summary>Adds a user root (no-op for the default root or duplicates). Returns the normalized path.</summary>
        public static string AddRoot(string path)
        {
            string full = Path.GetFullPath(path.Trim());
            if (string.Equals(full, DefaultRoot, StringComparison.OrdinalIgnoreCase))
                return full; // default is always implicit

            List<string> roots = LoadUserRoots().ToList();
            if (!roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(full);
                SaveUserRoots(roots);
            }
            return full;
        }

        /// <summary>Removes a user root (the default root cannot be removed).</summary>
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
