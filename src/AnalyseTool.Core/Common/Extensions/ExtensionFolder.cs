using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// The folder rules the machine-authoring commands share. They were private to SaveAsCommand while it
    /// was the only writer; SaveExtensionUi writes into the SAME folders, and two copies of "may I touch
    /// this directory" is exactly the pair that drifts apart and leaves one of them too permissive.
    /// </summary>
    internal static class ExtensionFolder
    {
        private static readonly Regex ValidId = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

        /// <summary>Extensions these commands write. A folder holding only these is one they may replace;
        /// anything else — a DLL, a year subfolder, a file someone put there — means hands off.</summary>
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
            string fullRoot = Path.GetFullPath(root);
            return Path.GetFullPath(directory)
                .StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? directory
                : null;
        }

        /// <summary>A registered extension source, or null when the caller named something else. Empty
        /// means whichever root the user picked in Settings for generated scripts — the AI saving a
        /// command has no opinion on where the user keeps their work.</summary>
        public static string? ResolveTargetRoot(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return ExtensionSources.AuthoringRoot;

            string full = Path.GetFullPath(requested!.Trim());
            return ExtensionSources.Roots()
                .Any(r => string.Equals(Path.GetFullPath(r), full, StringComparison.OrdinalIgnoreCase))
                ? full
                : null;
        }

        /// <summary>
        /// Whether a folder holds only what these commands write, and may therefore be overwritten by
        /// them. Judged by what is IN it rather than by a marker file: a marker can be copied into a
        /// folder that was never generated, while "everything here is a flat text file of a kind we
        /// write" cannot be true of a DLL extension or of a build output.
        /// </summary>
        public static bool IsGeneratedFolder(string directory)
        {
            if (!Directory.Exists(directory)) return true;    // nothing to protect yet
            if (Directory.GetDirectories(directory).Length > 0) return false;

            return Directory.GetFiles(directory)
                .All(path => GeneratedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()));
        }
    }
}
