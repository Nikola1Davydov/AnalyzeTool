using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>
    /// Turns <c>source:&lt;name&gt;/&lt;relative&gt;</c> references into a concrete https URL or
    /// file-system path on THIS seat (design §9). A <c>path</c> or <c>url</c> source resolves
    /// trivially; a <c>sharepoint</c> source is looked up in three steps, each a fallback for the one
    /// before: OneDrive's own library-to-folder mapping, a marker file found across the OneDrive
    /// mount points, and a folder the user picked once (stored in the user layer).
    /// </summary>
    internal static class PolicySourceResolver
    {
        public const string Prefix = "source:";
        public const string MarkerFileName = "analysetool-source.json";

        private static readonly object Gate = new();
        private static readonly Dictionary<string, string> Unresolved = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string>? _overrides;

        private static string OverridesFile => Path.Combine(PathProvider.ProfilePath, "sources.json");

        public static bool IsReference(string value) =>
            value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Splits <c>source:name/relative</c>. Null when the string is not a reference.</summary>
        public static (string Name, string Relative)? Parse(string reference)
        {
            if (!IsReference(reference)) return null;
            string rest = reference[Prefix.Length..].Trim();
            int cut = rest.IndexOfAny(new[] { '/', '\\' });
            return cut < 0
                ? (rest, string.Empty)
                : (rest[..cut], rest[(cut + 1)..].TrimStart('/', '\\'));
        }

        /// <summary>Resolves a reference to a URL or path. Anything that is not a reference is
        /// returned unchanged. Null (with <paramref name="problem"/>) when the source is unknown or
        /// cannot be located on this seat; the problem is also remembered for the status page.</summary>
        public static string? Resolve(string reference, out string? problem)
        {
            problem = null;
            (string Name, string Relative)? parsed = Parse(reference);
            if (parsed is null) return reference;

            (string name, string relative) = parsed.Value;
            string? root = ResolveRoot(name, out problem);
            if (root is null)
            {
                lock (Gate) Unresolved[name] = problem ?? "unresolved";
                return null;
            }
            lock (Gate) Unresolved.Remove(name);

            if (relative.Length == 0) return root;
            return root.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? root.TrimEnd('/') + "/" + relative.Replace('\\', '/')
                : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Sources that could not be located on this seat, with the reason — the Organization
        /// panel turns these into "Where is &lt;name&gt; on this computer?".</summary>
        public static IReadOnlyDictionary<string, string> UnresolvedSources()
        {
            lock (Gate) return new Dictionary<string, string>(Unresolved, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The user's answer to "where is this source": remembered per seat, never in the policy.</summary>
        public static void SetOverride(string name, string? path)
        {
            lock (Gate)
            {
                Dictionary<string, string> overrides = Overrides();
                if (string.IsNullOrWhiteSpace(path)) overrides.Remove(name);
                else overrides[name] = Path.GetFullPath(path.Trim());
                try
                {
                    Directory.CreateDirectory(PathProvider.ProfilePath);
                    File.WriteAllText(OverridesFile, JsonConvert.SerializeObject(overrides, Formatting.Indented));
                }
                catch (Exception ex) { Log.Warning(ex, "Could not persist source overrides"); }
                Unresolved.Remove(name);
            }
        }

        public static IReadOnlyDictionary<string, string> CurrentOverrides()
        {
            lock (Gate) return new Dictionary<string, string>(Overrides(), StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> Overrides()
        {
            if (_overrides is not null) return _overrides;
            try
            {
                if (File.Exists(OverridesFile))
                    _overrides = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(OverridesFile))
                        is { } loaded
                        ? new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase)
                        : null;
            }
            catch (Exception ex) { Log.Warning(ex, "Could not read source overrides"); }
            return _overrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string? ResolveRoot(string name, out string? problem)
        {
            problem = null;
            PolicyState policy = PolicyStore.Current;
            if (!policy.IsPresent || policy.Document.Sources is null
                || !policy.Document.Sources.TryGetValue(name, out PolicySource? source) || source is null)
            {
                problem = $"The policy declares no source named '{name}'.";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(source.Url))
            {
                if (!source.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    problem = $"Source '{name}': url must be https.";
                    return null;
                }
                return source.Url.Trim();
            }

            if (!string.IsNullOrWhiteSpace(source.Path))
            {
                string path = PolicySourceReader.ResolvePath(source.Path);
                if (Directory.Exists(path)) return path;
                problem = $"Source '{name}': folder '{path}' was not found.";
                return null;
            }

            // A user-picked folder wins over any automatic lookup: it is the one answer that came
            // from a human looking at this very machine.
            Dictionary<string, string> overrides;
            lock (Gate) overrides = Overrides();
            if (overrides.TryGetValue(name, out string? chosen) && Directory.Exists(chosen))
                return chosen;

            if (!string.IsNullOrWhiteSpace(source.Sharepoint))
            {
                string? mounted = OneDriveLibraries.FindLocalFolder(source.Sharepoint);
                if (mounted is not null) return mounted;
            }
            if (!string.IsNullOrWhiteSpace(source.MarkerId))
            {
                string? marked = OneDriveLibraries.FindByMarker(source.MarkerId);
                if (marked is not null) return marked;
            }

            problem = string.IsNullOrWhiteSpace(source.Sharepoint) && string.IsNullOrWhiteSpace(source.MarkerId)
                ? $"Source '{name}' declares neither url, path, sharepoint nor markerId."
                : $"Source '{name}' is not synced on this computer (or not found). " +
                  (string.IsNullOrWhiteSpace(source.SyncUrl) ? "Pick its folder once in Settings → Organization." : "Connect the library from Settings → Organization, or pick its folder once.");
            return null;
        }
    }

    /// <summary>
    /// Where the OneDrive client put a SharePoint library on this machine. Two lookups, both
    /// best-effort and Windows-only: the client's own settings (registry + <c>*.ini</c> under
    /// <c>%LOCALAPPDATA%\Microsoft\OneDrive\settings</c>), and a marker file found by walking the
    /// mount points a few levels deep.
    /// </summary>
    internal static class OneDriveLibraries
    {
        private static readonly Regex Quoted = new("\"([^\"]*)\"", RegexOptions.Compiled);

        /// <summary>The local folder for a library or folder URL, or null.</summary>
        public static string? FindLocalFolder(string sharepointUrl)
        {
            if (!Uri.TryCreate(sharepointUrl.Trim(), UriKind.Absolute, out Uri? wanted)) return null;

            foreach ((Uri libraryUrl, string localPath) in LibraryMappings())
            {
                string? match = MatchLibrary(wanted, libraryUrl, localPath);
                if (match is not null && Directory.Exists(match)) return match;
            }
            return null;
        }

        /// <summary>Pure: if <paramref name="wanted"/> lies inside <paramref name="library"/>, the
        /// local folder for it (mount point + the remaining segments), else null.</summary>
        internal static string? MatchLibrary(Uri wanted, Uri library, string localPath)
        {
            if (!string.Equals(wanted.Host, library.Host, StringComparison.OrdinalIgnoreCase)) return null;

            string[] want = Segments(wanted);
            string[] lib = Segments(library);
            if (want.Length < lib.Length) return null;
            for (int i = 0; i < lib.Length; i++)
                if (!string.Equals(want[i], lib[i], StringComparison.OrdinalIgnoreCase)) return null;

            string result = localPath;
            foreach (string segment in want.Skip(lib.Length))
                result = Path.Combine(result, segment);
            return result;
        }

        private static string[] Segments(Uri uri) =>
            Uri.UnescapeDataString(uri.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>(library URL, local folder) pairs the OneDrive client recorded. Read from the
        /// <c>*.ini</c> files next to the account settings: each <c>libraryScope</c> line carries a
        /// quoted site/library URL and a quoted local path. Undocumented, so this parses defensively —
        /// any quoted https URL on a line together with any quoted existing folder counts.</summary>
        public static IEnumerable<(Uri Library, string LocalPath)> LibraryMappings()
        {
            List<(Uri, string)> result = new();
            string settings = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "settings");
            if (!Directory.Exists(settings)) return result;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(settings, "*.ini", SearchOption.AllDirectories); }
            catch { return result; }

            foreach (string file in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                foreach (string line in lines)
                {
                    if (!line.StartsWith("libraryScope", StringComparison.OrdinalIgnoreCase)
                        && !line.StartsWith("libraryFolder", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach ((Uri url, string local) in ParseMappingLine(line))
                        result.Add((url, local));
                }
            }
            return result;
        }

        /// <summary>Pure: every (https URL, local folder) pair that can be read off one settings line.</summary>
        internal static IEnumerable<(Uri Url, string LocalPath)> ParseMappingLine(string line)
        {
            List<Uri> urls = new();
            List<string> paths = new();
            foreach (Match m in Quoted.Matches(line))
            {
                string value = m.Groups[1].Value.Trim();
                if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(value, UriKind.Absolute, out Uri? u))
                    urls.Add(u);
                else if (value.Length > 3 && (value[1] == ':' || value.StartsWith(@"\\")))
                    paths.Add(value);
            }
            foreach (Uri url in urls)
                foreach (string path in paths)
                    yield return (url, path);
        }

        /// <summary>Walks the OneDrive mount points (env roots, the profile's tenant folders) up to
        /// three levels deep for <c>analysetool-source.json</c> whose <c>id</c> matches.</summary>
        public static string? FindByMarker(string markerId)
        {
            foreach (string root in MountPoints())
            {
                string? found = ScanForMarker(root, markerId, depth: 3);
                if (found is not null) return found;
            }
            return null;
        }

        public static IEnumerable<string> MountPoints()
        {
            HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
            foreach (string var in new[] { "OneDriveCommercial", "OneDrive", "OneDriveConsumer" })
            {
                string? value = Environment.GetEnvironmentVariable(var);
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) roots.Add(value);
            }
            foreach ((_, string local) in LibraryMappings())
                if (Directory.Exists(local)) roots.Add(local);

            // Synced libraries land as <profile>\<Tenant>\<Site> - <Library>; the tenant folder is a
            // sibling of Documents/Desktop, so its children are the candidates.
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(profile))
            {
                IEnumerable<string> dirs;
                try { dirs = Directory.EnumerateDirectories(profile); } catch { dirs = Array.Empty<string>(); }
                foreach (string dir in dirs)
                {
                    IEnumerable<string> children;
                    try { children = Directory.EnumerateDirectories(dir); } catch { continue; }
                    foreach (string child in children)
                        if (Path.GetFileName(child).Contains(" - ", StringComparison.Ordinal)) roots.Add(child);
                }
            }
            return roots;
        }

        internal static string? ScanForMarker(string root, string markerId, int depth)
        {
            try
            {
                string marker = Path.Combine(root, PolicySourceResolver.MarkerFileName);
                if (File.Exists(marker))
                {
                    string? id = JObject.Parse(File.ReadAllText(marker))["id"]?.Value<string>();
                    if (string.Equals(id, markerId, StringComparison.OrdinalIgnoreCase)) return root;
                }
                if (depth <= 0) return null;
                foreach (string child in Directory.EnumerateDirectories(root))
                {
                    string? found = ScanForMarker(child, markerId, depth - 1);
                    if (found is not null) return found;
                }
            }
            catch { /* unreadable folder: not the one */ }
            return null;
        }
    }
}
