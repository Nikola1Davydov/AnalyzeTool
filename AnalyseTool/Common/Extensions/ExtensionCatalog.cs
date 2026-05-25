using Newtonsoft.Json;
using System.IO;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>One discovered extension: its parsed manifest plus the folder it lives in.</summary>
    internal sealed record ExtensionDescriptor(ExtensionManifest Manifest, string Directory)
    {
        public bool HasCommands => !string.IsNullOrWhiteSpace(Manifest.EntryAssembly);
        public bool HasUi => Manifest.Ui?.Button is not null;
    }

    /// <summary>
    /// Scans version-scoped extension directories (<c>&lt;root&gt;\&lt;revitVersion&gt;</c>) and returns the
    /// discovered manifests. The Revit version is implied by the folder, so there is no per-manifest
    /// version check. Shared by the C# command loader (Bootstrap) and the ribbon builder so discovery
    /// logic lives in exactly one place.
    /// </summary>
    internal static class ExtensionCatalog
    {
        /// <summary>Scans one version directory, surfacing a dialog for malformed manifests.</summary>
        public static IReadOnlyList<ExtensionDescriptor> Scan(string versionDir)
        {
            List<ExtensionDescriptor> result = new();
            if (!Directory.Exists(versionDir)) return result;

            foreach (string dir in Directory.GetDirectories(versionDir))
            {
                try
                {
                    string manifestPath = Path.Combine(dir, "plugin.json");
                    if (!File.Exists(manifestPath)) continue;

                    ExtensionManifest? manifest = JsonConvert.DeserializeObject<ExtensionManifest>(File.ReadAllText(manifestPath));
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                        throw new InvalidOperationException("plugin.json is missing the required 'id' field.");

                    result.Add(new ExtensionDescriptor(manifest, dir));
                }
                catch (Exception ex)
                {
                    UserDialogUtils.Error($"Failed to read extension manifest in '{dir}': {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>Scans several version directories (one per source root) and aggregates the result.</summary>
        public static IReadOnlyList<ExtensionDescriptor> Scan(IEnumerable<string> versionDirs) =>
            versionDirs.SelectMany(Scan).ToList();

        /// <summary>Enumerates every extension in a version directory without surfacing dialogs — for the
        /// Settings page listing. Unreadable folders are skipped.</summary>
        public static IReadOnlyList<ExtensionDescriptor> EnumerateAll(string versionDir)
        {
            List<ExtensionDescriptor> result = new();
            if (!Directory.Exists(versionDir)) return result;

            foreach (string dir in Directory.GetDirectories(versionDir))
            {
                try
                {
                    string manifestPath = Path.Combine(dir, "plugin.json");
                    if (!File.Exists(manifestPath)) continue;

                    ExtensionManifest? manifest = JsonConvert.DeserializeObject<ExtensionManifest>(File.ReadAllText(manifestPath));
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) continue;

                    result.Add(new ExtensionDescriptor(manifest, dir));
                }
                catch { /* skip unreadable manifests */ }
            }

            return result;
        }

        /// <summary>Enumerates extensions across several version directories.</summary>
        public static IReadOnlyList<ExtensionDescriptor> EnumerateAll(IEnumerable<string> versionDirs) =>
            versionDirs.SelectMany(EnumerateAll).ToList();
    }
}
