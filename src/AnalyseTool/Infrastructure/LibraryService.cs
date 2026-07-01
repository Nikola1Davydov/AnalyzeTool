using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Infrastructure
{
    /// <summary>
    /// Browses on-disk Revit family libraries: lists <c>.rfa</c> files under configured folders, flags the
    /// ones already loaded in the document (by family name), and loads selected ones into the model. Lets
    /// the palette's Library mode add families from disk to the current project.
    /// </summary>
    public sealed class LibraryService
    {
        // Revit writes numbered backups next to a family (e.g. "Door.0003.rfa"); skip those.
        private static readonly Regex BackupSuffix = new(@"\.\d{4}$", RegexOptions.Compiled);

        /// <summary>Lists the families under the given folders (recursively), tagging each with the root
        /// folder it came from and whether a family of that name is already loaded in the document.</summary>
        // Matches a 4-digit Revit year (e.g. "2024") inside BasicFileInfo.Format.
        private static readonly Regex YearInFormat = new(@"20\d{2}", RegexOptions.Compiled);

        public IReadOnlyList<LibraryFamily> List(Document doc, IEnumerable<string> folders)
        {
            HashSet<string> loaded = new(
                new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            int.TryParse(YearInFormat.Match(doc.Application.VersionNumber).Value, out int currentYear);

            List<LibraryFamily> result = new();
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

                    (string? version, bool compatible) = ReadVersion(file, currentYear);
                    result.Add(new LibraryFamily(file, name, folder, loaded.Contains(name), version, compatible));
                }
            }

            return result
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Reads the Revit version a family was saved in via <see cref="BasicFileInfo"/> (no open),
        /// and whether it can be loaded here (a family saved in a NEWER Revit can't be loaded into an older
        /// one). Returns (null, true) if the info can't be read.</summary>
        private static (string? version, bool compatible) ReadVersion(string path, int currentYear)
        {
            try
            {
                BasicFileInfo info = BasicFileInfo.Extract(path);
                Match m = YearInFormat.Match(info.Format ?? string.Empty);
                if (!m.Success) return (info.Format, true);

                bool compatible = currentYear == 0 || !int.TryParse(m.Value, out int y) || y <= currentYear;
                return (m.Value, compatible);
            }
            catch
            {
                return (null, true);
            }
        }

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
        [property: JsonProperty("compatible")] bool Compatible);
}
