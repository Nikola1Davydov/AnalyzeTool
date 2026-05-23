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
    /// Scans the extensions folder and returns the manifests compatible with the running Revit.
    /// Shared by the C# command loader (Bootstrap) and the ribbon builder (Launcher/OnStartup) so
    /// discovery logic lives in exactly one place.
    /// </summary>
    internal static class ExtensionCatalog
    {
        public static IReadOnlyList<ExtensionDescriptor> Scan(string extensionsRoot, string hostRevit)
        {
            List<ExtensionDescriptor> result = new();
            if (!Directory.Exists(extensionsRoot)) return result;

            foreach (string dir in Directory.GetDirectories(extensionsRoot))
            {
                try
                {
                    string manifestPath = Path.Combine(dir, "plugin.json");
                    if (!File.Exists(manifestPath)) continue;

                    ExtensionManifest? manifest = JsonConvert.DeserializeObject<ExtensionManifest>(File.ReadAllText(manifestPath));
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                        throw new InvalidOperationException("plugin.json is missing the required 'id' field.");

                    if (!string.Equals(manifest.TargetRevit, hostRevit, StringComparison.OrdinalIgnoreCase))
                    {
                        UserDialogUtils.Error($"Extension '{manifest.Id}' targets {manifest.TargetRevit} but host is {hostRevit}. Skipped.");
                        continue;
                    }

                    result.Add(new ExtensionDescriptor(manifest, dir));
                }
                catch (Exception ex)
                {
                    UserDialogUtils.Error($"Failed to read extension manifest in '{dir}': {ex.Message}");
                }
            }

            return result;
        }
    }
}
