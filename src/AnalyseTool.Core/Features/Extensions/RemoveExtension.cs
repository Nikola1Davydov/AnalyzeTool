using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Uninstalls a MANAGED extension: deletes <c>extensions-dist\&lt;id&gt;</c> plus its script
    /// cache, clears its disabled flag (a future reinstall starts enabled) and reloads. Dev-zone
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
            Request? req = ctx.Payload.As<Request>();
            string? id = req?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Extension id is required.");
            if (!ExtensionPackage.IsValidId(id))
                throw new InvalidOperationException($"Invalid extension id: {id}");

            string target = Path.Combine(ExtensionSources.DefaultManagedRoot, id);
            if (!Directory.Exists(target))
            {
                // Give an actionable error when the id exists but in the wrong zone.
                bool existsInDev = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                    .Any(d => d.Zone == ExtensionZone.Dev &&
                              string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
                throw new InvalidOperationException(existsInDev
                    ? $"'{id}' is a dev extension — the manager only uninstalls managed packages. " +
                      "Delete its folder yourself (Open in Explorer) if you want it gone."
                    : $"No installed extension with id '{id}'.");
            }

            Directory.Delete(target, recursive: true);

            string scriptCache = PathProvider.ScriptCacheDir(id);
            if (Directory.Exists(scriptCache)) Directory.Delete(scriptCache, recursive: true);

            // Forget the disabled flag so a future reinstall starts enabled like any fresh install.
            ExtensionStateStore.SetEnabled(id, enabled: true);

            Log.Information("Uninstalled extension {Id}", id);
            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new { removed = true, id });
        }

        internal sealed record Request
        {
            [Description("Extension id (as reported by GetInstalledExtensions; managed zone only).")]
            public string Id { get; set; } = string.Empty;
        }
    }
}
