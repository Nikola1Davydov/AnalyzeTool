using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using Serilog;
using System.IO;
using System.Reflection;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// Loads the C# command half of user-authored extensions discovered by <see cref="ExtensionCatalog"/>.
    /// Extensions without an entryAssembly (JS-only) are skipped here — their UI is handled by the
    /// ribbon/window layer. Extensions the host builds from source are compiled first
    /// (<see cref="HostBuild"/>). One bad extension is logged and skipped; the rest still load.
    /// </summary>
    internal sealed class ExtensionLoader
    {
        private readonly CommandDispatcher _dispatcher;
        private readonly string _revitVersion;   // Revit version year, e.g. "2025"
        private readonly Version _hostSdkVersion; // the AnalyseTool.Sdk AssemblyVersion this host provides
        private readonly List<ExtensionLoadContext> _contexts = new();

        public ExtensionLoader(CommandDispatcher dispatcher, string revitVersion)
        {
            _dispatcher = dispatcher;
            _revitVersion = revitVersion;
            _hostSdkVersion = typeof(IRevitTask).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
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
            foreach (ExtensionDescriptor scanned in ExtensionCatalog.Scan(_revitVersion))
            {
                // User-disabled (extensions-state.json): stays listed in Settings, loads nothing.
                if (!ExtensionStateStore.IsEnabled(scanned.Manifest.Id)) continue;

                ExtensionDiagnostics.Clear(scanned.Manifest.Id);

                // Sources the host compiles are brought up to date first — which may migrate an old
                // script folder and changes what the descriptor resolves to, so it is read again.
                string? buildError = PrepareHostBuild(scanned, out ExtensionDescriptor descriptor);

                // Nothing to load and a reason why (a script folder that could not be migrated, sources
                // that never compiled): say so rather than skipping the extension in silence.
                if (buildError is not null && !descriptor.HasDll)
                {
                    ExtensionDiagnostics.SetError(descriptor.Manifest.Id, buildError);
                    Log.Warning("Extension {Id}: {Error}", descriptor.Manifest.Id, buildError);
                    continue;
                }

                // Declared a DLL but ships no build for this Revit year: listed as incompatible,
                // never loaded — surface WHY in diagnostics instead of failing on a missing file.
                if (descriptor.DeclaresDll && !descriptor.HasDll)
                {
                    string error = $"No build for Revit {_revitVersion}: " +
                        $"'{descriptor.Manifest.EntryAssembly}' not found in '{_revitVersion}\\' or the extension root.";
                    ExtensionDiagnostics.SetError(descriptor.Manifest.Id, error);
                    Log.Warning("Extension {Id}: {Error}", descriptor.Manifest.Id, error);
                    continue;
                }

                if (!descriptor.HasCommands) continue; // JS-only extension, nothing to load here

                try
                {
                    LoadDllCommands(descriptor);
                }
                catch (Exception ex)
                {
                    // No dialog from Core: the error is logged and lands in ExtensionDiagnostics,
                    // which the Settings extension listing surfaces to the user.
                    ExtensionDiagnostics.SetError(descriptor.Manifest.Id, ex.Message);
                    Log.Error(ex, "Failed to load extension {Id}", descriptor.Manifest.Id);
                    continue;
                }

                // The edit did not compile, but an earlier build did and is loaded: the commands keep
                // working, and the error says why the change has not taken effect.
                if (buildError is not null)
                    ExtensionDiagnostics.SetError(descriptor.Manifest.Id,
                        "The sources did not compile; the previous build is still in use." +
                        Environment.NewLine + buildError);
            }
        }

        /// <summary>
        /// Migrates a script folder of the old format, and compiles a host-built extension whose build
        /// for this Revit year is missing or older than its sources (<see cref="HostBuild"/>). Returns
        /// the compiler's errors, or null; <paramref name="current"/> is the descriptor as it reads now.
        /// </summary>
        private string? PrepareHostBuild(ExtensionDescriptor scanned, out ExtensionDescriptor current)
        {
            current = scanned;
            string id = scanned.Manifest.Id;
            string directory = scanned.Directory;
            bool changed = false;

            if (scanned.IsLegacyLayout && HostBuild.IsScriptFolder(directory, scanned.Manifest))
                return "Script extensions are no longer compiled from the old extensions\\<year>\\<id> layout. " +
                       $"Move the folder to extensions\\{id} and reload: it is then built as a DLL.";

            if (!scanned.IsLegacyLayout && HostBuild.IsScriptFolder(directory, scanned.Manifest))
            {
                string? migrationError = HostBuild.MigrateScriptFolder(directory, id);
                if (migrationError is not null) return migrationError;
                Log.Information("Extension {Id}: script sources moved to {Folder}\\ — it is built as a DLL from now on",
                    id, HostBuild.SourceFolder);
                changed = true;
            }

            string? error = null;
            if (HostBuild.IsHostBuilt(directory))
            {
                ExtensionDescriptor manifestNow = changed ? ExtensionCatalog.Reread(scanned, _revitVersion) ?? scanned : scanned;
                string? entryAssembly = manifestNow.Manifest.EntryAssembly;
                if (string.IsNullOrWhiteSpace(entryAssembly))
                {
                    // Sources in src\ but no entryAssembly — put there by hand. Without it the build
                    // below would never be found, so the manifest is completed first.
                    entryAssembly = HostBuild.EntryAssemblyFor(id);
                    string? manifestError = HostBuild.SetEntryAssembly(directory, entryAssembly);
                    if (manifestError is not null) return manifestError;
                    changed = true;
                }

                if (HostBuild.NeedsBuild(directory, entryAssembly!, _revitVersion))
                {
                    ScriptCompileResult result = HostBuild.Build(directory, entryAssembly!, _revitVersion);
                    if (result.Success)
                    {
                        Log.Information("Extension {Id}: built {Assembly} for Revit {Year}", id, entryAssembly, _revitVersion);
                        changed = true;
                    }
                    else
                    {
                        error = string.Join(Environment.NewLine, result.Errors);
                        Log.Warning("Extension {Id} failed to compile: {Errors}", id, error);
                    }
                }
            }

            if (changed) current = ExtensionCatalog.Reread(scanned, _revitVersion) ?? scanned;
            return error;
        }

        private void LoadDllCommands(ExtensionDescriptor descriptor)
        {
            ExtensionManifest manifest = descriptor.Manifest;

            // Resolved by the catalog for the running Revit version: <dir>\<year>\<entry> first,
            // then <dir>\<entry>. Guard again for the delete-between-scan-and-load race.
            string entryPath = descriptor.EntryAssemblyPath!;
            if (!File.Exists(entryPath))
                throw new FileNotFoundException($"Entry assembly '{manifest.EntryAssembly}' not found.", entryPath);

            // Track the context BEFORE anything that can throw. A collectible ALC that was created but
            // never reached _contexts is unreachable and can never be unloaded — and the paths below
            // throw routinely (a half-written DLL, a corrupt image), so the leak accumulates exactly
            // when the author is reloading most: while fixing a broken build.
            ExtensionLoadContext alc = new ExtensionLoadContext(entryPath, manifest.Id);
            _contexts.Add(alc);

            Assembly assembly = alc.LoadEntry(entryPath); // byte-load: does not lock the DLL on disk

            // Compatibility is derived from the SDK the DLL was actually built against (its
            // AnalyseTool.Sdk reference) — no hand-written manifest field to keep in sync.
            Version? referencedSdk = assembly.GetReferencedAssemblies()
                .FirstOrDefault(a => string.Equals(a.Name, "AnalyseTool.Sdk", StringComparison.OrdinalIgnoreCase))?.Version;

            // Different major = breaking contract change → reject cleanly.
            if (referencedSdk == null || referencedSdk.Major != _hostSdkVersion.Major)
            {
                string error = $"Built against AnalyseTool.Sdk {(referencedSdk?.ToString() ?? "<none>")}, " +
                    $"incompatible with host SDK {_hostSdkVersion.Major}.x. Skipped.";
                ExtensionDiagnostics.SetError(manifest.Id, error);
                Log.Warning("Extension {Id}: {Error}", manifest.Id, error);
                UnloadAndForget(alc); // collectible context — drop the rejected assembly
                return;
            }

            // Same major but built against a NEWER minor than the host provides: the extension may call
            // API added after this host's SDK (minors are additive), which would throw MissingMethod at
            // runtime. Reject at load with a clear "update the plugin" message instead. (Older minors are
            // fine — the shared host copy is version-agnostic, see ExtensionLoadContext.)
            if (referencedSdk > _hostSdkVersion)
            {
                string error = $"Built against AnalyseTool.Sdk {referencedSdk}, newer than this plugin's " +
                    $"SDK {_hostSdkVersion}. Update the plugin to use it. Skipped.";
                ExtensionDiagnostics.SetError(manifest.Id, error);
                Log.Warning("Extension {Id}: {Error}", manifest.Id, error);
                UnloadAndForget(alc);
                return;
            }

            _dispatcher.RegisterExtension(assembly, manifest.Id);
            Log.Information("Loaded DLL extension {Id} (SDK {Sdk})", manifest.Id, referencedSdk);
        }

        /// <summary>Unloads a context we decided not to use and stops tracking it, so the next
        /// <see cref="UnloadAll"/> doesn't try to unload it a second time.</summary>
        private void UnloadAndForget(ExtensionLoadContext context)
        {
            _contexts.Remove(context);
            try { context.Unload(); }
            catch { /* references may still be alive; GC collects later */ }
        }
    }
}
