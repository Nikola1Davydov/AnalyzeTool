using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Uninstalls a MANAGED extension: deletes its folder under <c>extensions-dist</c> plus its
    /// script cache, clears its disabled flag (a future reinstall starts enabled) and reloads. The
    /// folder is resolved from the CATALOG (folder name and manifest id may differ) and parked as
    /// <c>.old</c> before deletion, so a locked file can never leave a half-alive install. Dev-zone
    /// extensions are the author's own folders — the manager never deletes those.</summary>
    [RevitCommand(
        Description = "Uninstalls a managed extension (removes its folder from extensions-dist); applies via reload.",
        InputType = typeof(RemoveExtension.Request),
        Destructive = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class RemoveExtension : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string? id = ctx.Payload.As<Request>()?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Extension id is required.");

            IReadOnlyList<ExtensionDescriptor> descriptors = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion);
            ExtensionDescriptor? managed = descriptors.FirstOrDefault(d =>
                d.Zone == ExtensionZone.Managed &&
                string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

            if (managed is null)
            {
                bool existsInDev = descriptors.Any(d =>
                    d.Zone == ExtensionZone.Dev &&
                    string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
                throw new InvalidOperationException(existsInDev
                    ? $"'{id}' is a dev extension — the manager only uninstalls managed packages. " +
                      "Delete its folder yourself (Open in Explorer) if you want it gone."
                    : $"No installed extension with id '{id}'.");
            }

            // Defense-in-depth: whatever the catalog returned must live under the managed root.
            string target = Path.GetFullPath(managed.Directory);
            string fullRoot = Path.GetFullPath(ExtensionSources.DefaultManagedRoot);
            if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to delete outside the managed root: {target}");

            // Park first, then delete: the rename atomically removes the extension from the catalog
            // (*.old is skipped), and a file lock during the delete leaves only an ignored husk
            // instead of a half-alive install.
            string parking = target + ".old";
            if (Directory.Exists(parking)) TryDelete(parking);
            Directory.Move(target, parking);
            TryDelete(parking);

            string scriptCache = PathProvider.ScriptCacheDir(id);
            if (Directory.Exists(scriptCache)) TryDelete(scriptCache);

            // Forget the disabled flag so a future reinstall starts enabled — but only when no OTHER
            // extension shares this id (a deliberately disabled dev twin must stay disabled).
            bool twinRemains = descriptors.Any(d =>
                !ReferenceEquals(d, managed) &&
                string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (!twinRemains)
                ExtensionStateStore.SetEnabled(id, enabled: true);

            Log.Information("Uninstalled extension {Id} from {Directory}", id, managed.Directory);
            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new { removed = true, id });
        }

        private static void TryDelete(string directory)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception ex)
            {
                // Best-effort: a lingering *.old folder is invisible to the catalog and cleaned up
                // by the next install/uninstall pass.
                Log.Warning(ex, "Could not fully delete {Directory}; leftover is ignored by the scanner", directory);
            }
        }

        internal sealed record Request
        {
            [Description("Extension id (as reported by GetInstalledExtensions; managed zone only).")]
            public string Id { get; set; } = string.Empty;
        }
    }
}
