using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Deletes a DEV extension's folder — the counterpart to <see cref="RemoveExtension"/>, which
    /// handles the managed zone and refuses these on purpose.
    ///
    /// That refusal made sense while a dev folder meant "something the author built by hand and the
    /// manager has no business touching". It stopped making sense once a session could generate ten of
    /// them: telling someone to go and delete ten folders in Explorer, one at a time, is not a safety
    /// measure, it is a chore that makes them stop tidying up.
    ///
    /// So the folder goes, with two things kept from the managed path — park-then-delete, so a locked
    /// file cannot leave a half-alive extension behind, and a root check, so whatever the catalog
    /// handed back must actually live under a registered dev root.
    /// </summary>
    [RevitCommand(
        Description = "Deletes a dev extension's folder and reloads. Dev zone only — installed packages " +
                      "go through RemoveExtension.",
        InputType = typeof(RemoveDevExtension.Request),
        Destructive = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class RemoveDevExtension : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string? id = ctx.Payload.As<Request>()?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Extension id is required.");

            IReadOnlyList<ExtensionDescriptor> descriptors = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion);
            ExtensionDescriptor? dev = descriptors.FirstOrDefault(d =>
                d.Zone == ExtensionZone.Dev &&
                string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

            if (dev is null)
            {
                bool managedTwin = descriptors.Any(d =>
                    d.Zone == ExtensionZone.Managed &&
                    string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
                throw new InvalidOperationException(managedTwin
                    ? $"'{id}' is an installed package — uninstall it from the Installed section."
                    : $"No dev extension with id '{id}'.");
            }

            string target = Path.GetFullPath(dev.Directory);
            if (!UnderADevRoot(target))
                throw new InvalidOperationException($"Refusing to delete outside the extension roots: {target}");

            // A git checkout is the one thing in here that can hold work no copy on disk has. Everything
            // else a folder might carry — a build, a project file, compiled output — comes back from its
            // source; uncommitted history does not. So this is the single case that is handed back to the
            // person instead of deleted for them.
            if (Directory.Exists(Path.Combine(target, ".git")))
                throw new InvalidOperationException(
                    $"'{id}' is a git working folder. Delete it yourself (Open in Explorer) — it may hold " +
                    "commits or changes that exist nowhere else.");

            // Park first, then delete: the rename atomically removes the extension from the catalog
            // (*.old is skipped), and a file lock during the delete leaves only an ignored husk.
            string parking = target + ".old";
            if (Directory.Exists(parking)) TryDelete(parking);
            Directory.Move(target, parking);
            TryDelete(parking);

            string scriptCache = PathProvider.ScriptCacheDir(id);
            if (Directory.Exists(scriptCache)) TryDelete(scriptCache);

            // Forget the disabled flag, unless another folder still carries this id — a deliberately
            // disabled twin must stay disabled.
            bool twinRemains = descriptors.Any(d =>
                !ReferenceEquals(d, dev) &&
                string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (!twinRemains)
                ExtensionStateStore.SetEnabled(id, enabled: true);

            // The user's ribbon preference for this extension's commands outlives the folder otherwise:
            // a pinned button for a command that no longer exists is pruned from the ribbon but stays in
            // the store, and would come back if the id were ever reused.
            foreach (CommandButtonPin pin in CommandButtons.Pinned())
                if (pin.Command.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
                    CommandButtons.Set(pin.Command, null);

            Log.Information("Deleted dev extension {Id} from {Directory}", id, dev.Directory);
            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new { removed = true, id, directory = dev.Directory });
        }

        /// <summary>Defence in depth: the catalog only ever reports folders under a registered root, and
        /// this is what makes that a checked fact rather than an assumption at a delete.</summary>
        private static bool UnderADevRoot(string target) =>
            ExtensionSources.AllRoots()
                .Where(r => r.Zone == ExtensionZone.Dev)
                .Any(r => target.StartsWith(
                    Path.GetFullPath(r.Path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));

        private static void TryDelete(string directory)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception ex)
            {
                // Best-effort: a lingering *.old folder is invisible to the catalog and cleaned up by
                // the next install/uninstall pass.
                Log.Warning(ex, "Could not fully delete {Directory}; leftover is ignored by the scanner", directory);
            }
        }

        internal sealed record Request
        {
            [Description("Extension id (as reported by GetInstalledExtensions; dev zone only).")]
            public string Id { get; set; } = string.Empty;
        }
    }
}
