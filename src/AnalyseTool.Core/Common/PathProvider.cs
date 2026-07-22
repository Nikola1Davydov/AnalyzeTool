using System.IO;
using System.Reflection;

namespace AnalyseTool.Core.Common
{
    internal sealed class PathProvider
    {
        // typeof-based (not GetExecutingAssembly) so the value stays pinned to this assembly's folder
        // no matter which assembly of the split platform calls it.
        public static string RootDirectory => Path.GetDirectoryName(typeof(PathProvider).Assembly.Location)!;
        public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SharedData.ToolData.PLUGIN_NAME);

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\extensions — the MANAGED extensions root, owned by the
        /// Extension Manager (packages are installed/removed/updated here). Extensions live directly
        /// under it (<c>extensions\&lt;id&gt;</c>) with optional per-Revit-year binary subfolders
        /// (<c>&lt;id&gt;\2025\...</c>). The legacy layout <c>extensions\&lt;year&gt;\&lt;id&gt;</c> is still scanned.</summary>
        public static string ExtensionsRoot => Path.Combine(ProfilePath, "extensions");

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\extensions-dev — the default DEV extensions root: loose
        /// folders authored by the user (scripts, templates, work-in-progress), not managed packages.
        /// New templates are scaffolded here.</summary>
        public static string ExtensionsDevRoot => Path.Combine(ProfilePath, "extensions-dev");

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\cache\scripts\&lt;id&gt; — compiled bytes of a script extension,
        /// keyed by a source hash so unchanged scripts skip recompilation across Revit sessions.</summary>
        public static string ScriptCacheDir(string extensionId) =>
            Path.Combine(ProfilePath, "cache", "scripts", extensionId);

        public static string DebugServerUrl => "http://localhost:22524";
    }
}
