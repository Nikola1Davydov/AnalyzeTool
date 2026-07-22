using Newtonsoft.Json;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>One discovered extension: its parsed manifest, the folder it lives in, the zone it came
    /// from, and the entry assembly resolved for the RUNNING Revit version (null when the extension
    /// declares one but ships no build for this year — i.e. incompatible with this host).</summary>
    internal sealed record ExtensionDescriptor(
        ExtensionManifest Manifest,
        string Directory,
        ExtensionZone Zone,
        bool IsLegacyLayout,
        string? EntryAssemblyPath,
        IReadOnlyList<string> BinaryYears)
    {
        /// <summary>The manifest names an <c>entryAssembly</c> (whether or not a build exists for this year).</summary>
        public bool DeclaresDll => !string.IsNullOrWhiteSpace(Manifest.EntryAssembly);

        /// <summary>Prebuilt-DLL extension with a build resolved for the running Revit version.</summary>
        public bool HasDll => EntryAssemblyPath is not null;

        /// <summary>Script extension: no entryAssembly, but the folder ships C# source compiled by Roslyn.
        /// (When entryAssembly IS set, any <c>.cs</c> are treated as build sources and ignored here.)</summary>
        public bool HasScript => !DeclaresDll && ScriptFiles.Count > 0;

        /// <summary>True if the extension contributes commands loadable by THIS host.</summary>
        public bool HasCommands => HasDll || HasScript;

        public bool HasUi => Manifest.Ui?.Button is not null;

        /// <summary>False only when a declared entry assembly has no build for the running Revit
        /// version — such extensions are listed (as incompatible) but never loaded.</summary>
        public bool IsCompatibleWithHost => !DeclaresDll || HasDll;

        /// <summary>Top-level <c>.cs</c> files in the extension folder (the Roslyn script sources).
        /// Scripts are version-independent, so they always live in the folder root.</summary>
        public IReadOnlyList<string> ScriptFiles =>
            System.IO.Directory.Exists(Directory)
                ? System.IO.Directory.GetFiles(Directory, "*.cs")
                : Array.Empty<string>();
    }

    /// <summary>
    /// Scans extension source roots and returns the discovered manifests. Two layouts are recognized:
    /// <list type="bullet">
    /// <item>Current: <c>&lt;root&gt;\&lt;id&gt;\plugin.json</c>, with optional per-Revit-year binary subfolders
    /// (<c>&lt;id&gt;\2025\Ext.dll</c>). The entry assembly resolves year-folder-first, then folder root;
    /// scripts and UI always come from the root.</item>
    /// <item>Legacy (deprecated): <c>&lt;root&gt;\&lt;year&gt;\&lt;id&gt;\plugin.json</c> — only the running year's
    /// container is scanned.</item>
    /// </list>
    /// Shared by the C# command loader (Bootstrap) and the ribbon builder so discovery logic lives in
    /// exactly one place.
    /// </summary>
    internal static class ExtensionCatalog
    {
        private static readonly Regex YearDir = new(@"^\d{4}$", RegexOptions.Compiled);

        /// <summary>Scans all source roots for the running Revit version, recording diagnostics for
        /// malformed manifests (strict mode — used by the loader and the ribbon builder).</summary>
        public static IReadOnlyList<ExtensionDescriptor> Scan(string revitVersion) =>
            ExtensionSources.AllRoots().SelectMany(r => ScanRoot(r, revitVersion, strict: true)).ToList();

        /// <summary>Enumerates every extension across all roots without recording diagnostics — for the
        /// Settings page listing. Unreadable folders are skipped.</summary>
        public static IReadOnlyList<ExtensionDescriptor> EnumerateAll(string revitVersion) =>
            ExtensionSources.AllRoots().SelectMany(r => ScanRoot(r, revitVersion, strict: false)).ToList();

        /// <summary>Scans one source root (both layouts). In strict mode malformed manifests are logged
        /// and land in <see cref="ExtensionDiagnostics"/>; otherwise they are silently skipped.</summary>
        public static IReadOnlyList<ExtensionDescriptor> ScanRoot(ExtensionSourceRoot root, string revitVersion, bool strict)
        {
            List<ExtensionDescriptor> result = new();
            if (!Directory.Exists(root.Path)) return result;

            foreach (string dir in Directory.GetDirectories(root.Path))
            {
                if (YearDir.IsMatch(Path.GetFileName(dir)))
                {
                    // Legacy container <root>\<year>\<id> — only the running year contributes.
                    if (!string.Equals(Path.GetFileName(dir), revitVersion, StringComparison.Ordinal))
                        continue;
                    foreach (string legacyDir in Directory.GetDirectories(dir))
                        AddDescriptor(result, legacyDir, root.Zone, isLegacy: true, revitVersion, strict);
                }
                else
                {
                    AddDescriptor(result, dir, root.Zone, isLegacy: false, revitVersion, strict);
                }
            }

            return result;
        }

        private static void AddDescriptor(List<ExtensionDescriptor> result, string dir, ExtensionZone zone,
            bool isLegacy, string revitVersion, bool strict)
        {
            try
            {
                string manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) return;

                ExtensionManifest? manifest = JsonConvert.DeserializeObject<ExtensionManifest>(File.ReadAllText(manifestPath));
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    if (!strict) return;
                    throw new InvalidOperationException("plugin.json is missing the required 'id' field.");
                }

                result.Add(new ExtensionDescriptor(
                    manifest, dir, zone, isLegacy,
                    ResolveEntryAssembly(manifest, dir, isLegacy, revitVersion),
                    BinaryYears(dir, isLegacy)));
            }
            catch (Exception ex)
            {
                if (!strict) return; // Settings listing: skip unreadable manifests silently
                // Log-only (no dialog from Core); the broken folder shows up in diagnostics keyed
                // by its directory name, so the Settings listing can still explain what's wrong.
                Serilog.Log.Error(ex, "Failed to read extension manifest in {Directory}", dir);
                ExtensionDiagnostics.SetError(Path.GetFileName(dir), $"Invalid plugin.json: {ex.Message}");
            }
        }

        /// <summary>Resolves the declared entry assembly for the running Revit version:
        /// <c>&lt;dir&gt;\&lt;year&gt;\&lt;entry&gt;</c> first, then <c>&lt;dir&gt;\&lt;entry&gt;</c> (legacy folders only
        /// ever use the latter). Null when declared but not present — incompatible with this host.</summary>
        private static string? ResolveEntryAssembly(ExtensionManifest manifest, string dir, bool isLegacy, string revitVersion)
        {
            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) return null;

            if (!isLegacy)
            {
                string versioned = Path.Combine(dir, revitVersion, manifest.EntryAssembly!);
                if (File.Exists(versioned)) return versioned;
            }

            string plain = Path.Combine(dir, manifest.EntryAssembly!);
            return File.Exists(plain) ? plain : null;
        }

        /// <summary>Year-named binary subfolders of a current-layout extension (empty for legacy) —
        /// lets the manager display which Revit versions an extension ships builds for.</summary>
        private static IReadOnlyList<string> BinaryYears(string dir, bool isLegacy)
        {
            if (isLegacy || !Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetDirectories(dir)
                .Select(Path.GetFileName)
                .Where(name => name is not null && YearDir.IsMatch(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
