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

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\extensions — the default DEV extensions root, exactly what
        /// this folder has always been: loose folders authored by the user (scripts, templates,
        /// work-in-progress). Extensions live directly under it (<c>extensions\&lt;id&gt;</c>) with optional
        /// per-Revit-year binary subfolders (<c>&lt;id&gt;\2025\...</c>); the legacy
        /// <c>extensions\&lt;year&gt;\&lt;id&gt;</c> layout is still scanned. New templates are scaffolded here.</summary>
        public static string ExtensionsRoot => Path.Combine(ProfilePath, "extensions");

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\extensions-dist — the MANAGED extensions root, owned by the
        /// Extension Manager: distributed packages are installed/removed/updated here. Deliberately a
        /// NEW folder, so the manager starts with clean invariants and nothing changes for existing
        /// hand-managed extensions.</summary>
        public static string ExtensionsDistRoot => Path.Combine(ProfilePath, "extensions-dist");

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\cache\scripts\&lt;id&gt; — compiled bytes of a script extension,
        /// keyed by a source hash so unchanged scripts skip recompilation across Revit sessions.</summary>
        public static string ScriptCacheDir(string extensionId) =>
            Path.Combine(ProfilePath, "cache", "scripts", extensionId);

        public static string DebugServerUrl => "http://localhost:22524";
    }
}
