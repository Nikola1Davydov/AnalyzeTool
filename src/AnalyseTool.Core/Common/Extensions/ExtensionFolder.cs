using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>Where a save lands, or why it cannot. Exactly one of the two is set.</summary>
    internal sealed record SaveTarget(string? Directory, string? Error);

    /// <summary>
    /// The folder rules the machine-authoring commands share. They were private to SaveAsCommand while it
    /// was the only writer; SaveExtensionUi writes into the SAME folders, and two copies of "may I touch
    /// this directory" is exactly the pair that drifts apart and leaves one of them too permissive.
    /// </summary>
    internal static class ExtensionFolder
    {
        private static readonly Regex ValidId = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

        /// <summary>Files these commands write into an extension's root. <c>.cs</c> stays for a script
        /// folder not yet migrated to <c>src\</c> (that happens on its next load).</summary>
        private static readonly string[] GeneratedExtensions =
            { ".cs", ".json", ".html", ".htm", ".css", ".js", ".mjs", ".svg", ".md", ".txt" };

        public static bool IsValidId(string id) => ValidId.IsMatch(id);

        /// <summary>Null when the name is a plain file name that may be written into an extension folder,
        /// otherwise why not. Rejecting separators outright is simpler than resolving paths and then
        /// arguing about which of them escape.</summary>
        public static string? ValidateFileName(string? name, IReadOnlyCollection<string> allowedExtensions)
        {
            string candidate = name?.Trim() ?? string.Empty;
            if (candidate.Length == 0) return "A file has no name.";

            if (candidate.Contains('/') || candidate.Contains('\\') || candidate.Contains("..") ||
                Path.IsPathRooted(candidate) || candidate != Path.GetFileName(candidate))
                return $"'{candidate}' is not a plain file name — files are written flat into the extension folder.";

            string extension = Path.GetExtension(candidate).ToLowerInvariant();
            return allowedExtensions.Contains(extension)
                ? null
                : $"'{candidate}' has an extension these commands do not write. Allowed: {string.Join(", ", allowedExtensions)}.";
        }

        /// <summary>The extension directory inside <paramref name="root"/>, or null when the id would
        /// escape it. Defence in depth: the id is already character-checked, and this catches whatever
        /// that check ever fails to.</summary>
        public static string? ResolveExtensionDirectory(string root, string id)
        {
            string directory = Path.Combine(root, id);
            // Trimmed before the separator is appended: a root that already ends in one — "D:\", which
            // is what GetFullPath returns for a drive — would otherwise be compared against "D:\\", and
            // nothing starts with that. Every save into such a root failed, blaming the id for it.
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return Path.GetFullPath(directory)
                .StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? directory
                : null;
        }

        /// <summary>
        /// The folder a save should write to. An explicit <paramref name="requestedRoot"/> is honoured
        /// as before; with none, the rule is WHERE THE EXTENSION ALREADY LIVES, and only a genuinely
        /// new id falls through to the authoring root.
        ///
        /// It used to be "the authoring root, always". That is right for a new extension and wrong for
        /// every edit of one that came from somewhere else — a team's shared extensions folder added as a
        /// source, most of all. Fixing a colleague's script wrote a SECOND folder with the same id into
        /// the user's own root: both load, both register the same command names, and which of them wins
        /// depends on the order the roots happen to have been added. Meanwhile the broken original sat
        /// untouched where the whole team could see it.
        ///
        /// Only DEV roots are searched. An id that exists solely as an installed package resolves to the
        /// authoring root instead: the Extension Manager owns that folder and its next update would put
        /// the old code back, so a local copy is the only honest answer.
        /// </summary>
        public static SaveTarget ResolveSaveDirectory(string id, string? requestedRoot)
        {
            if (!string.IsNullOrWhiteSpace(requestedRoot))
            {
                string? named = ResolveTargetRoot(requestedRoot);
                return named is null
                    ? new SaveTarget(null, $"'{requestedRoot}' is not a folder these commands may write " +
                                           "to — it is not a registered source, or it holds installed " +
                                           "packages the Extension Manager overwrites on update. Leave " +
                                           "targetRoot empty to save where the extension already lives.")
                    : Combine(named, id);
            }

            // Through ResolveExtensionDirectory rather than Path.Combine: an id is character-checked by
            // the callers, but '..' passes that check, and Directory.Exists on a combined path would
            // then happily match a folder outside the root.
            List<string> existing = ExtensionSources.AllRoots()
                .Where(r => r.Zone == ExtensionZone.Dev)
                .Select(r => ResolveExtensionDirectory(r.Path, id))
                .Where(d => d is not null && Directory.Exists(d))
                .Select(d => d!)
                .ToList();

            if (existing.Count > 1)
                return new SaveTarget(null,
                    $"'{id}' exists in more than one source folder ({string.Join("; ", existing)}). " +
                    "Name the one to write to with targetRoot.");

            return existing.Count == 1 ? new SaveTarget(existing[0], null) : Combine(ExtensionSources.AuthoringRoot, id);
        }

        private static SaveTarget Combine(string root, string id)
        {
            string? directory = ResolveExtensionDirectory(root, id);
            return directory is null
                ? new SaveTarget(null, "Invalid extension id (path escapes the extensions folder).")
                : new SaveTarget(directory, null);
        }

        /// <summary>
        /// A registered DEV source a save may be aimed at, or null. Empty means whichever root the user
        /// picked for saved commands — the AI saving a command has no opinion on where
        /// the user keeps their work.
        ///
        /// Managed roots are excluded, which they were not before: an explicit targetRoot could still
        /// aim a generated script at <c>extensions-dist</c>, the one folder both the implicit search and
        /// <see cref="ExtensionSources.SetAuthoringRoot"/> refuse — and the next package install or
        /// update there deletes whatever was written.
        /// </summary>
        public static string? ResolveTargetRoot(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return ExtensionSources.AuthoringRoot;

            string full = Path.GetFullPath(requested!.Trim());
            return ExtensionSources.AllRoots()
                .Any(r => r.Zone == ExtensionZone.Dev &&
                          string.Equals(Path.GetFullPath(r.Path), full, StringComparison.OrdinalIgnoreCase))
                ? full
                : null;
        }

        /// <summary>
        /// Whether a folder holds only what these commands write, and may therefore be overwritten by
        /// them. Judged by what is IN it rather than by a marker file: a marker can be copied into a
        /// folder that was never generated, while "everything here is a file of a kind we write, in a
        /// place we write it" cannot be true of a project someone builds or of foreign build output.
        ///
        /// That is: flat text files in the root, sources in <c>src\</c>, and the host's own build in
        /// year folders (<c>2025\&lt;id&gt;.dll</c> + <c>.pdb</c>). Anything else — a project file, a
        /// DLL in the root, another subfolder — means hands off.
        /// </summary>
        public static bool IsGeneratedFolder(string directory)
        {
            if (!Directory.Exists(directory)) return true;    // nothing to protect yet

            bool rootIsOurs = Directory.GetFiles(directory)
                .All(path => GeneratedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()));
            if (!rootIsOurs) return false;

            foreach (string sub in Directory.GetDirectories(directory))
            {
                string name = Path.GetFileName(sub);
                bool ours = string.Equals(name, HostBuild.SourceFolder, StringComparison.OrdinalIgnoreCase)
                    ? OnlyFiles(sub, ".cs")
                    : YearFolder.IsMatch(name) && OnlyFiles(sub, ".dll", ".pdb");
                if (!ours) return false;
            }
            return true;
        }

        private static readonly Regex YearFolder = new(@"^\d{4}$", RegexOptions.Compiled);

        private static bool OnlyFiles(string directory, params string[] extensions) =>
            Directory.GetDirectories(directory).Length == 0
            && Directory.GetFiles(directory)
                .All(path => extensions.Contains(Path.GetExtension(path).ToLowerInvariant()));
    }
}
