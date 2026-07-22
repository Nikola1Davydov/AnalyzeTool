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

            string prefix = LocateManifestPrefix(zip);
            ZipArchiveEntry manifestEntry = zip.GetEntry(prefix + "plugin.json")!;

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

            List<string> binaryYears = zip.Entries
                .Select(e => e.FullName.Replace('\\', '/'))
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
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
                bool atRoot = zip.GetEntry(prefix + entryName) is not null;
                bool inAnyYear = binaryYears.Any(y => zip.GetEntry($"{prefix}{y}/{entryName}") is not null);
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
                if (!name.StartsWith(info.Prefix, StringComparison.Ordinal)) continue;

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

        /// <summary>plugin.json at the zip root wins; otherwise exactly one top-level folder must
        /// contain it (the common "zipped the folder itself" shape).</summary>
        private static string LocateManifestPrefix(ZipArchive zip)
        {
            if (zip.GetEntry("plugin.json") is not null) return string.Empty;

            List<string> candidates = zip.Entries
                .Select(e => e.FullName.Replace('\\', '/'))
                .Where(n => n.Count(c => c == '/') == 1 && n.EndsWith("/plugin.json", StringComparison.Ordinal))
                .Select(n => n.Substring(0, n.IndexOf('/') + 1))
                .Distinct(StringComparer.Ordinal)
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

        internal static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (id.Contains("..", StringComparison.Ordinal)) return false;
            if (!char.IsLetterOrDigit(id[0])) return false;
            return id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
        }
    }
}
