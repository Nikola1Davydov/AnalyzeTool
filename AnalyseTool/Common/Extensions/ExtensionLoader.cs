using AnalyseTool.Common.Dispatch;
using AnalyseTool.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

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
            ExtensionDiagnostics.ClearAll();
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

                ExtensionDiagnostics.Clear(descriptor.Manifest.Id);
                try
                {
                    if (descriptor.HasDll)
                        LoadDllCommands(descriptor);
                    else
                        LoadScriptCommands(descriptor);
                }
                catch (Exception ex)
                {
                    ExtensionDiagnostics.SetError(descriptor.Manifest.Id, ex.Message);
                    UserDialogUtils.Error($"Failed to load extension '{descriptor.Manifest.Id}': {ex.Message}");
                }
            }
        }

        /// <summary>Compiles a script extension's C# source with Roslyn (cached by source hash) and
        /// registers the resulting commands — same downstream as a prebuilt DLL.</summary>
        private void LoadScriptCommands(ExtensionDescriptor descriptor)
        {
            string id = descriptor.Manifest.Id;
            string[] files = descriptor.ScriptFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

            string cacheDir = PathProvider.ScriptCacheDir(id);
            Directory.CreateDirectory(cacheDir);
            string cachedDll = Path.Combine(cacheDir, ScriptCacheKey(files) + ".dll");

            if (!File.Exists(cachedDll))
            {
                ScriptCompileResult result = RoslynScriptCompiler.CompileFiles(files, id);
                if (!result.Success)
                {
                    string error = string.Join(Environment.NewLine, result.Errors);
                    ExtensionDiagnostics.SetError(id, error);
                    UserDialogUtils.Error($"Extension '{id}' failed to compile:{Environment.NewLine}{error}");
                    return;
                }

                File.WriteAllBytes(cachedDll, result.Assembly!);
                if (result.Pdb is not null)
                    File.WriteAllBytes(Path.ChangeExtension(cachedDll, ".pdb"), result.Pdb);
            }

            ExtensionLoadContext alc = new ExtensionLoadContext(cachedDll, id);
            Assembly assembly = alc.LoadEntry(cachedDll); // byte-load: cache file stays unlocked
            _contexts.Add(alc);
            _dispatcher.RegisterExtension(assembly, id);
        }

        /// <summary>Cache key = SHA-256 over the script sources (+ a plugin-version salt so a host upgrade
        /// invalidates stale compiled bytes). Changed source ⇒ new key ⇒ recompile.</summary>
        private static string ScriptCacheKey(IEnumerable<string> files)
        {
            StringBuilder sb = new();
            sb.Append(SharedData.ToolData.PLUGIN_VERSION).Append('\n');
            foreach (string file in files)
            {
                sb.Append(Path.GetFileName(file)).Append('\n');
                sb.Append(File.ReadAllText(file)).Append('\n');
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash, 0, 8); // 16 hex chars is plenty for a per-id cache
        }

        private void LoadDllCommands(ExtensionDescriptor descriptor)
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
