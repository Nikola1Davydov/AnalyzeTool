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
        private readonly string _revitVersion;   // Revit version year, e.g. "2025"
        private readonly int _hostSdkMajor;
        private readonly List<ExtensionLoadContext> _contexts = new();

        public ExtensionLoader(CommandDispatcher dispatcher, string revitVersion)
        {
            _dispatcher = dispatcher;
            _revitVersion = revitVersion;
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

        public void LoadAll()
        {
            foreach (ExtensionDescriptor descriptor in ExtensionCatalog.Scan(ExtensionSources.ScanDirs(_revitVersion)))
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

            string entryPath = Path.Combine(descriptor.Directory, manifest.EntryAssembly!);
            if (!File.Exists(entryPath))
                throw new FileNotFoundException($"Entry assembly '{manifest.EntryAssembly}' not found.", entryPath);

            ExtensionLoadContext alc = new ExtensionLoadContext(entryPath, manifest.Id);
            Assembly assembly = alc.LoadEntry(entryPath); // byte-load: does not lock the DLL on disk

            // Compatibility is derived from the SDK the DLL was actually built against (its
            // AnalyseTool.Sdk reference) — no hand-written manifest field to keep in sync.
            Version? referencedSdk = assembly.GetReferencedAssemblies()
                .FirstOrDefault(a => string.Equals(a.Name, "AnalyseTool.Sdk", StringComparison.OrdinalIgnoreCase))?.Version;

            if (referencedSdk == null || referencedSdk.Major != _hostSdkMajor)
            {
                UserDialogUtils.Error($"Extension '{manifest.Id}' was built against AnalyseTool.Sdk " +
                    $"{(referencedSdk?.ToString() ?? "<none>")}, incompatible with host SDK {_hostSdkMajor}.x. Skipped.");
                alc.Unload(); // collectible context — drop the rejected assembly
                return;
            }

            _contexts.Add(alc);
            _dispatcher.RegisterExtension(assembly, manifest.Id);
        }
    }
}
