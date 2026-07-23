using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Browses on-disk Revit family libraries: lists <c>.rfa</c> files under configured folders, flags the
    /// ones already loaded in the document (by family name), and loads selected ones into the model. Lets
    /// the palette's Library mode add families from disk to the current project.
    ///
    /// Listing is split in two so a big (network) library doesn't freeze Revit: <see cref="ScanFiles"/> is
    /// pure file I/O and runs OFF the Revit thread; <see cref="Decorate"/> needs the API (loaded families,
    /// BasicFileInfo) and runs inside RunInRevitAsync, kept fast by caching version info per (path, mtime).
    /// </summary>
    public sealed class LibraryService
    {
        // Revit writes numbered backups next to a family (e.g. "Door.0003.rfa"); skip those.
        private static readonly Regex BackupSuffix = new(@"\.\d{4}$", RegexOptions.Compiled);

        // Matches a 4-digit Revit year (e.g. "2024") inside BasicFileInfo.Format.
        private static readonly Regex YearInFormat = new(@"20\d{2}", RegexOptions.Compiled);

        // BasicFileInfo.Extract is not free (opens the OLE container); cache the saved-in version per
        // (path, mtime) so rescans of an unchanged library skip the extraction entirely.
        private static readonly ConcurrentDictionary<(string Path, long MtimeTicks), string?> _versionCache = new();

        /// <summary>One .rfa found on disk, before any Revit-side decoration. Pure I/O product.</summary>
        public sealed record ScannedFile(string Path, string Name, string Folder, long MtimeTicks);

        /// <summary>Enumerates the .rfa files under the given folders (recursively). Pure file I/O — call
        /// OFF the Revit thread. Skips Revit backup files and de-dupes across overlapping roots.</summary>
        public static IReadOnlyList<ScannedFile> ScanFiles(IEnumerable<string> folders)
        {
            List<ScannedFile> result = new();
            HashSet<string> seenFiles = new(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in (folders ?? Enumerable.Empty<string>()).Where(Directory.Exists))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder, "*.rfa", SearchOption.AllDirectories); }
                catch { continue; } // unreadable / permission — skip the folder

                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (BackupSuffix.IsMatch(name)) continue;      // family.0001.rfa backup
                    if (!seenFiles.Add(file)) continue;            // de-dupe across overlapping roots

                    long mtime = 0;
                    try { mtime = File.GetLastWriteTimeUtc(file).Ticks; } catch { /* best-effort */ }
                    result.Add(new ScannedFile(file, name, folder, mtime));
                }
            }

            return result.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Decorates scanned files with document knowledge: whether a family of that name is
        /// already loaded, and the Revit version the file was saved in (cached by path+mtime). Needs the
        /// Revit API — call inside RunInRevitAsync; the cache keeps repeat scans cheap.</summary>
        public IReadOnlyList<LibraryFamily> Decorate(Document doc, IReadOnlyList<ScannedFile> files)
        {
            HashSet<string> loaded = new(
                new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            int.TryParse(YearInFormat.Match(doc.Application.VersionNumber).Value, out int currentYear);

            List<LibraryFamily> result = new(files.Count);
            foreach (ScannedFile f in files)
            {
                string? version = _versionCache.GetOrAdd((f.Path, f.MtimeTicks), key => ReadVersion(key.Path));
                bool compatible = IsCompatible(version, currentYear);
                result.Add(new LibraryFamily(f.Path, f.Name, f.Folder, loaded.Contains(f.Name),
                    version, compatible, f.MtimeTicks));
            }

            return result;
        }

        /// <summary>Reads the Revit version a family was saved in via <see cref="BasicFileInfo"/> (no open).
        /// Returns the 4-digit year when recognizable, the raw format string otherwise, null on failure.</summary>
        private static string? ReadVersion(string path)
        {
            try
            {
                BasicFileInfo info = BasicFileInfo.Extract(path);
                Match m = YearInFormat.Match(info.Format ?? string.Empty);
                return m.Success ? m.Value : info.Format;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A family saved in a NEWER Revit can't be loaded into an older one. Unknown versions are
        /// treated as compatible (LoadFamily will be the final judge).</summary>
        private static bool IsCompatible(string? version, int currentYear) =>
            currentYear == 0 || version is null || !int.TryParse(version, out int y) || y <= currentYear;

        /// <summary>Loads one family file into the document. Returns false if it couldn't be loaded (e.g. a
        /// family of that name already exists, or the file is invalid). Own transaction + warning swallow.</summary>
        public bool LoadOne(Document doc, string path)
        {
            if (!File.Exists(path)) return false;

            try
            {
                using Transaction t = new(doc, "Family Manager: load family");
                t.Start();
                SwallowWarningsPreprocessor.Apply(t);
                bool ok = doc.LoadFamily(path, out Family _);
                t.Commit();
                return ok;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed record LibraryFamily(
        [property: JsonProperty("path")] string Path,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("folder")] string Folder,
        [property: JsonProperty("loaded")] bool Loaded,
        [property: JsonProperty("version")] string? Version,
        [property: JsonProperty("compatible")] bool Compatible,
        // File last-write time (UTC ticks) — the frontend uses it as the preview-cache version so an
        // edited .rfa gets a fresh thumbnail.
        [property: JsonProperty("fileTime")] long FileTime);
}
