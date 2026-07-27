using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>Validated view of an extension package: its manifest, the in-zip prefix of the
    /// extension folder ("" when plugin.json sits at the zip root, else "folder/"), and the Revit
    /// years the package ships binaries for.</summary>
    internal sealed record ExtensionPackageInfo(
        ExtensionManifest Manifest,
        string Prefix,
        IReadOnlyList<string> BinaryYears);

    /// <summary>
    /// The distribution contract: a package is a ZIP of the extension folder — <c>plugin.json</c> at
    /// the zip root or inside exactly one top-level folder, optional per-Revit-year binary subfolders
    /// (<c>2025/Ext.dll</c>), scripts/UI at the folder root. ONE shared validator serves both sides
    /// (vendor-side pack and user-side install), so a package that packs is a package that installs.
    /// All failures throw <see cref="InvalidOperationException"/> with a message an extension author
    /// (or their AI assistant) can act on directly.
    /// </summary>
    internal static class ExtensionPackage
    {
        private static readonly Regex YearDir = new(@"^\d{4}$", RegexOptions.Compiled);

        public static ExtensionPackageInfo Validate(string zipPath)
        {
            if (!File.Exists(zipPath))
                throw new InvalidOperationException($"Package not found: {zipPath}");

            using ZipArchive zip = OpenZip(zipPath);

            // Windows PowerShell's Compress-Archive stores entry names with BACKSLASHES; never use
            // ZipArchive.GetEntry (exact match on the raw name) — resolve through one normalized,
            // case-insensitive map, matching how the file system itself would behave.
            Dictionary<string, ZipArchiveEntry> entries = NormalizedEntries(zip);

            string prefix = LocateManifestPrefix(entries.Keys);
            if (!entries.TryGetValue(prefix + "plugin.json", out ZipArchiveEntry? manifestEntry))
                throw new InvalidOperationException("No plugin.json found in the package.");

            ExtensionManifest? manifest;
            using (StreamReader reader = new(manifestEntry.Open()))
                manifest = JsonConvert.DeserializeObject<ExtensionManifest>(reader.ReadToEnd());

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                throw new InvalidOperationException("plugin.json is missing the required 'id' field.");
            if (!IsValidId(manifest.Id))
                throw new InvalidOperationException(
                    $"Invalid extension id '{manifest.Id}': only letters, digits, '.', '-' and '_' are allowed.");
            if (string.IsNullOrWhiteSpace(manifest.Version))
                throw new InvalidOperationException("plugin.json is missing the required 'version' field.");

            // Every entry must extract INSIDE the target folder — reject traversal and absolute paths.
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.Split('/').Any(seg => seg == "..") || Path.IsPathRooted(name))
                    throw new InvalidOperationException($"Package contains an unsafe path: {entry.FullName}");
            }

            List<string> binaryYears = entries.Keys
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Substring(prefix.Length))
                .Where(n => n.Contains('/'))
                .Select(n => n.Split('/')[0])
                .Where(seg => YearDir.IsMatch(seg))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(y => y, StringComparer.Ordinal)
                .ToList();

            // A declared entry assembly must be present for at least one Revit year (or at the root).
            if (!string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            {
                string entryName = manifest.EntryAssembly!.Replace('\\', '/');
                bool atRoot = entries.ContainsKey(prefix + entryName);
                bool inAnyYear = binaryYears.Any(y => entries.ContainsKey($"{prefix}{y}/{entryName}"));
                if (!atRoot && !inAnyYear)
                    throw new InvalidOperationException(
                        $"entryAssembly '{manifest.EntryAssembly}' is declared but the package contains no " +
                        "such file — expected it in a Revit-year folder (e.g. '2025/') or at the folder root.");
            }

            return new ExtensionPackageInfo(manifest, prefix, binaryYears);
        }

        /// <summary>Extracts the (already validated) package into <paramref name="targetDir"/>,
        /// stripping the in-zip prefix. Refuses to write outside the target.</summary>
        public static void ExtractTo(string zipPath, ExtensionPackageInfo info, string targetDir)
        {
            using ZipArchive zip = OpenZip(zipPath);
            string fullTarget = Path.GetFullPath(targetDir);
            Directory.CreateDirectory(fullTarget);

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(info.Prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string relative = name.Substring(info.Prefix.Length);
                if (relative.Length == 0) continue;

                string destination = Path.GetFullPath(Path.Combine(fullTarget, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Package entry escapes the target folder: {entry.FullName}");

                if (relative.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        private static ZipArchive OpenZip(string zipPath)
        {
            try
            {
                return ZipFile.OpenRead(zipPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Not a readable zip package: {ex.Message}");
            }
        }

        /// <summary>All zip entries keyed by their NORMALIZED name: backslashes → forward slashes
        /// (Windows Compress-Archive stores backslashes), case-insensitive like the file system.</summary>
        private static Dictionary<string, ZipArchiveEntry> NormalizedEntries(ZipArchive zip)
        {
            Dictionary<string, ZipArchiveEntry> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in zip.Entries)
                map[entry.FullName.Replace('\\', '/')] = entry;
            return map;
        }

        /// <summary>plugin.json at the zip root wins; otherwise exactly one top-level folder must
        /// contain it (the common "zipped the folder itself" shape). Names are pre-normalized.</summary>
        private static string LocateManifestPrefix(IEnumerable<string> normalizedNames)
        {
            List<string> names = normalizedNames.ToList();
            if (names.Any(n => string.Equals(n, "plugin.json", StringComparison.OrdinalIgnoreCase)))
                return string.Empty;

            List<string> candidates = names
                .Where(n => n.Count(c => c == '/') == 1 && n.EndsWith("/plugin.json", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Substring(0, n.IndexOf('/') + 1))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return candidates.Count switch
            {
                1 => candidates[0],
                0 => throw new InvalidOperationException(
                    "No plugin.json found — expected it at the zip root or inside one top-level folder."),
                _ => throw new InvalidOperationException(
                    "The zip contains several top-level folders with plugin.json — package exactly ONE extension."),
            };
        }

        // Win32 device names are unusable as folder names ("con", "nul", also "con.anything").
        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        internal static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (id.Contains("..", StringComparison.Ordinal)) return false;
            // First and last char must be alphanumeric: a trailing '.' would collide with another
            // folder (Win32 strips trailing dots — 'acme.' and 'acme' are the SAME directory).
            if (!char.IsLetterOrDigit(id[0]) || !char.IsLetterOrDigit(id[^1])) return false;
            if (!id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')) return false;
            // The installer's own work-folder suffixes would make the extension invisible to the
            // catalog (it skips *.installing / *.old).
            if (id.EndsWith(".installing", StringComparison.OrdinalIgnoreCase)
                || id.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) return false;
            // Win32 reserved device names apply to the segment before the first dot.
            string firstSegment = id.Split('.')[0];
            if (ReservedNames.Contains(firstSegment)) return false;
            return true;
        }
    }
}
