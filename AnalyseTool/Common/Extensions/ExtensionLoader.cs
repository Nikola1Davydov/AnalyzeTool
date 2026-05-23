using AnalyseTool.Common.Dispatch;
using AnalyseTool.Sdk;
using System.IO;
using System.Reflection;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>
    /// Loads the C# command half of user-authored extensions discovered by <see cref="ExtensionCatalog"/>.
    /// Extensions without an entryAssembly (JS-only) are skipped here — their UI is handled by the
    /// ribbon/window layer. One bad extension is logged and skipped; the rest still load.
    /// </summary>
    internal sealed class ExtensionLoader
    {
        private readonly CommandDispatcher _dispatcher;
        private readonly string _hostRevit;   // "R25" / "R26"
        private readonly int _hostSdkMajor;
        private readonly List<ExtensionLoadContext> _contexts = new();

        public ExtensionLoader(CommandDispatcher dispatcher, string hostRevit)
        {
            _dispatcher = dispatcher;
            _hostRevit = hostRevit;
            _hostSdkMajor = typeof(IRevitTask).Assembly.GetName().Version?.Major ?? 1;
        }

        /// <summary>Drops loaded extension commands and unloads their (collectible) contexts so a
        /// subsequent <see cref="LoadAll"/> picks up changed DLLs without restarting Revit.</summary>
        public void UnloadAll()
        {
            _dispatcher.ClearExtensions();
            foreach (ExtensionLoadContext context in _contexts)
            {
                try { context.Unload(); }
                catch { /* references may still be alive; GC collects later */ }
            }
            _contexts.Clear();
        }

        public void LoadAll(string extensionsRoot)
        {
            foreach (ExtensionDescriptor descriptor in ExtensionCatalog.Scan(extensionsRoot, _hostRevit))
            {
                if (!descriptor.HasCommands) continue; // JS-only extension, nothing to load here

                try
                {
                    LoadCommands(descriptor);
                }
                catch (Exception ex)
                {
                    UserDialogUtils.Error($"Failed to load extension '{descriptor.Manifest.Id}': {ex.Message}");
                }
            }
        }

        private void LoadCommands(ExtensionDescriptor descriptor)
        {
            ExtensionManifest manifest = descriptor.Manifest;

            if (!IsSdkCompatible(manifest.SdkVersion))
            {
                UserDialogUtils.Error($"Extension '{manifest.Id}' was built against SDK '{manifest.SdkVersion}', incompatible with host SDK {_hostSdkMajor}.x. Skipped.");
                return;
            }

            string entryPath = Path.Combine(descriptor.Directory, manifest.EntryAssembly!);
            if (!File.Exists(entryPath))
                throw new FileNotFoundException($"Entry assembly '{manifest.EntryAssembly}' not found.", entryPath);

            ExtensionLoadContext alc = new ExtensionLoadContext(entryPath, manifest.Id);
            _contexts.Add(alc);
            Assembly assembly = alc.LoadEntry(entryPath); // byte-load: does not lock the DLL on disk
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
