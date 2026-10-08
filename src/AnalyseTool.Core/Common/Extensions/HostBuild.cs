using AnalyseTool.Core.Common.Extensions.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// Extensions the HOST builds: C# sources in <c>&lt;ext&gt;\src\</c>, compiled by Roslyn into an ordinary
    /// DLL extension at <c>&lt;ext&gt;\&lt;year&gt;\&lt;entryAssembly&gt;</c>.
    ///
    /// This is what replaced script extensions. There used to be two kinds of command extension — a
    /// script (loose <c>.cs</c> the host compiled into a cache at every load) and a DLL (a project the
    /// author builds) — and people could not tell them apart, nor why one said "Script" and the other
    /// "Not built". Now there is one kind: a DLL. Saving a command (<c>SaveAsCommand</c>) compiles it on
    /// the spot, so the person who saves needs no .NET SDK, no NuGet and no build step; the sources stay
    /// beside it so an agent can read and fix them.
    ///
    /// A DLL is compiled against ONE Revit year's API (net8 for 2025/2026, net10 for 2027), so a folder
    /// saved under one version is rebuilt from <c>src\</c> the first time another version loads it — and
    /// whenever a source is newer than the build, which is how a hand edit takes effect on Reload.
    ///
    /// A project the author builds with <c>dotnet build</c> is never touched here: a <c>.csproj</c> in the
    /// folder means the author owns the build.
    /// </summary>
    internal static class HostBuild
    {
        /// <summary>The folder the host compiles from. Not scanned for scripts, not shipped by
        /// <c>PackExtension</c> (it excludes <c>**\*.cs</c>).</summary>
        public const string SourceFolder = "src";

        /// <summary>The folder's sources, in a stable order.</summary>
        public static IReadOnlyList<string> SourceFiles(string directory)
        {
            string src = Path.Combine(directory, SourceFolder);
            return Directory.Exists(src)
                ? Directory.GetFiles(src, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                : Array.Empty<string>();
        }

        /// <summary>True when the host owns this folder's build: there are sources in <c>src\</c> and no
        /// project file anywhere the author could be building from.</summary>
        public static bool IsHostBuilt(string directory) =>
            SourceFiles(directory).Count > 0 && !HasProject(directory);

        /// <summary>A script extension in the format before host builds: loose <c>.cs</c> in the folder
        /// root and no <c>entryAssembly</c>. It is migrated on load (<see cref="MigrateScriptFolder"/>).</summary>
        public static bool IsScriptFolder(string directory, ExtensionManifest manifest) =>
            string.IsNullOrWhiteSpace(manifest.EntryAssembly)
            && Directory.Exists(directory)
            && Directory.GetFiles(directory, "*.cs").Length > 0
            && !HasProject(directory);

        /// <summary>The entry assembly file name the host writes for an extension id.</summary>
        public static string EntryAssemblyFor(string id) => id + ".dll";

        /// <summary>
        /// Moves a script folder's root <c>.cs</c> into <c>src\</c> and records the entry assembly, so the
        /// next build turns it into a DLL extension. The command names do not change: they are
        /// <c>&lt;id&gt;.&lt;class&gt;</c> either way, so buttons, MCP clients and pages keep working.
        /// Returns null on success, otherwise why it could not.
        /// </summary>
        public static string? MigrateScriptFolder(string directory, string id)
        {
            try
            {
                string src = Path.Combine(directory, SourceFolder);
                Directory.CreateDirectory(src);
                foreach (string file in Directory.GetFiles(directory, "*.cs"))
                {
                    string target = Path.Combine(src, Path.GetFileName(file));
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(file, target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Could not move the scripts of '{id}' into {SourceFolder}\\: {ex.Message}";
            }

            return SetEntryAssembly(directory, EntryAssemblyFor(id));
        }

        /// <summary>Whether the build for <paramref name="revitVersion"/> is missing or older than any
        /// source — the two cases in which the host compiles.</summary>
        public static bool NeedsBuild(string directory, string entryAssembly, string revitVersion)
        {
            string dll = Path.Combine(directory, revitVersion, entryAssembly);
            if (!File.Exists(dll)) return true;

            DateTime built = File.GetLastWriteTimeUtc(dll);
            return SourceFiles(directory).Any(f => File.GetLastWriteTimeUtc(f) > built);
        }

        /// <summary>Compiles every source in <c>src\</c> into <c>&lt;year&gt;\&lt;entryAssembly&gt;</c>. On
        /// failure nothing is written, so an earlier build — if any — stays in place and keeps loading.</summary>
        public static ScriptCompileResult Build(string directory, string entryAssembly, string revitVersion)
        {
            IReadOnlyList<string> files = SourceFiles(directory);
            List<(string Path, string Text)> sources;
            try
            {
                sources = files.Select(f => (f, File.ReadAllText(f))).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ScriptCompileResult(null, null, new[] { $"Cannot read the sources: {ex.Message}" });
            }

            ScriptCompileResult result = RoslynScriptCompiler.CompileSources(
                sources, Path.GetFileNameWithoutExtension(entryAssembly));
            if (result.Success) Write(directory, entryAssembly, revitVersion, result);
            return result;
        }

        /// <summary>Writes a compiled assembly (and its PDB) where the catalog resolves the entry
        /// assembly for this Revit year. The loader reads it as bytes, so the file is never locked and
        /// can be replaced while the old version is loaded.</summary>
        public static void Write(string directory, string entryAssembly, string revitVersion, ScriptCompileResult result)
        {
            string yearDir = Path.Combine(directory, revitVersion);
            Directory.CreateDirectory(yearDir);
            string dll = Path.Combine(yearDir, entryAssembly);
            File.WriteAllBytes(dll, result.Assembly!);

            string pdb = Path.ChangeExtension(dll, ".pdb");
            if (result.Pdb is not null) File.WriteAllBytes(pdb, result.Pdb);
            else if (File.Exists(pdb)) File.Delete(pdb);
        }

        private static bool HasProject(string directory) =>
            Directory.Exists(directory)
            && Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).Any();

        /// <summary>Sets <c>entryAssembly</c> in place, leaving every other field as it is. Null on
        /// success, otherwise why it could not.</summary>
        public static string? SetEntryAssembly(string directory, string entryAssembly)
        {
            try
            {
                string path = Path.Combine(directory, "plugin.json");
                JObject manifest = JObject.Parse(File.ReadAllText(path));
                manifest["entryAssembly"] = entryAssembly;
                File.WriteAllText(path, manifest.ToString(Formatting.Indented));
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return $"Could not record entryAssembly in plugin.json: {ex.Message}";
            }
        }
    }
}
