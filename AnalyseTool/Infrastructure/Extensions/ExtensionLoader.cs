using AnalyseTool.Infrastructure.Dispatch;
using AnalyseTool.Sdk;
using AnalyseTool.Utils;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;

namespace AnalyseTool.Infrastructure.Extensions
{
    /// <summary>
    /// Discovers and loads user-authored C# command extensions from the extensions folder.
    /// Each extension lives in its own sub-folder with a <c>plugin.json</c> manifest and its
    /// assembly. One bad extension is logged and skipped; the rest still load.
    /// </summary>
    internal sealed class ExtensionLoader
    {
        private readonly CommandDispatcher _dispatcher;
        private readonly string _hostRevit;   // "R25" / "R26"
        private readonly int _hostSdkMajor;

        public ExtensionLoader(CommandDispatcher dispatcher, string hostRevit)
        {
            _dispatcher = dispatcher;
            _hostRevit = hostRevit;
            _hostSdkMajor = typeof(IRevitTask).Assembly.GetName().Version?.Major ?? 1;
        }

        public void LoadAll(string extensionsRoot)
        {
            if (!Directory.Exists(extensionsRoot)) return;

            foreach (string dir in Directory.GetDirectories(extensionsRoot))
            {
                try
                {
                    LoadOne(dir);
                }
                catch (Exception ex)
                {
                    UserDialogUtils.Error($"Failed to load extension from '{dir}': {ex.Message}");
                }
            }
        }

        private void LoadOne(string dir)
        {
            string manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) return;   // not an extension folder

            ExtensionManifest? manifest = JsonConvert.DeserializeObject<ExtensionManifest>(File.ReadAllText(manifestPath));
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                throw new InvalidOperationException("plugin.json is missing required fields (id, entryAssembly).");

            if (!string.Equals(manifest.TargetRevit, _hostRevit, StringComparison.OrdinalIgnoreCase))
            {
                UserDialogUtils.Error($"Extension '{manifest.Id}' targets {manifest.TargetRevit} but host is {_hostRevit}. Skipped.");
                return;
            }

            if (!IsSdkCompatible(manifest.SdkVersion))
            {
                UserDialogUtils.Error($"Extension '{manifest.Id}' was built against SDK '{manifest.SdkVersion}', incompatible with host SDK {_hostSdkMajor}.x. Skipped.");
                return;
            }

            string entryPath = Path.Combine(dir, manifest.EntryAssembly);
            if (!File.Exists(entryPath))
                throw new FileNotFoundException($"Entry assembly '{manifest.EntryAssembly}' not found.", entryPath);

            ExtensionLoadContext alc = new ExtensionLoadContext(entryPath, manifest.Id);
            Assembly assembly = alc.LoadFromAssemblyPath(entryPath);
            _dispatcher.RegisterExtension(assembly, manifest.Id);
        }

        private bool IsSdkCompatible(string manifestSdkVersion)
        {
            if (string.IsNullOrWhiteSpace(manifestSdkVersion)) return false;

            // Major-version compatibility: "1.0", "1.2.3" and "1" all map to major 1.
            string majorPart = manifestSdkVersion.Split('.')[0];
            return int.TryParse(majorPart, out int major) && major == _hostSdkMajor;
        }
    }
}
