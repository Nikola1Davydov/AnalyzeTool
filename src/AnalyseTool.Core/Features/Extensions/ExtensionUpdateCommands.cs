using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Serilog;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Polls each MANAGED extension's <c>updateFeed</c> and reports which have newer
    /// versions. Network call; nothing is downloaded or changed. Dev-zone extensions are the
    /// author's working copies — they have no update semantics and are skipped.</summary>
    [RevitCommand(
        Description = "Checks the update feeds of installed extensions and reports available updates. " +
                      "Network call; does not touch the Revit model.",
        ReadOnly = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class CheckExtensionUpdates : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            List<ExtensionDescriptor> candidates = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                .Where(d => d.Zone == ExtensionZone.Managed && !string.IsNullOrWhiteSpace(d.Manifest.UpdateFeed))
                .ToList();

            IEnumerable<Task<object>> checks = candidates.Select(async d =>
            {
                string id = d.Manifest.Id;
                string installed = d.Manifest.Version;
                try
                {
                    ExtensionUpdateInfo latest = await ExtensionUpdateFeed.ResolveAsync(d.Manifest.UpdateFeed!, ct);
                    return (object)new
                    {
                        id,
                        installed,
                        latest = latest.Version,
                        updateAvailable = IsNewer(latest.Version, installed),
                        releaseUrl = latest.ReleaseUrl,
                        error = (string?)null,
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warning(ex, "Update check failed for extension {Id}", id);
                    return (object)new
                    {
                        id,
                        installed,
                        latest = (string?)null,
                        updateAvailable = false,
                        releaseUrl = (string?)null,
                        error = ex.Message,
                    };
                }
            });

            object[] results = await Task.WhenAll(checks);
            return new { results };
        }

        /// <summary>SemVer-ish comparison; when either side does not parse, any difference counts as
        /// an update (the vendor's feed is the authority on what "current" means).</summary>
        internal static bool IsNewer(string latest, string installed)
        {
            if (Version.TryParse(Normalize(latest), out Version? l) &&
                Version.TryParse(Normalize(installed), out Version? i))
                return l > i;
            return !string.Equals(latest.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);

            static string Normalize(string v)
            {
                string trimmed = v.Trim().TrimStart('v', 'V');
                int cut = trimmed.IndexOfAny(new[] { '-', '+' }); // strip prerelease/build metadata
                return cut < 0 ? trimmed : trimmed[..cut];
            }
        }
    }

    /// <summary>Downloads the latest package from the vendor's <c>updateFeed</c> and replaces the
    /// installed version (staging + backup swap — a failed update restores the old install). The
    /// original install already carried the logged third-party consent; updates come from the same
    /// vendor-declared feed.</summary>
    [RevitCommand(
        Description = "Updates an installed extension from its update feed; applies via reload.",
        InputType = typeof(UpdateExtension.Request),
        Destructive = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class UpdateExtension : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string? id = ctx.Payload.As<Request>()?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Extension id is required.");

            ExtensionDescriptor? installed = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                .FirstOrDefault(d => d.Zone == ExtensionZone.Managed &&
                                     string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (installed is null)
                throw new InvalidOperationException($"No installed extension with id '{id}'.");
            if (string.IsNullOrWhiteSpace(installed.Manifest.UpdateFeed))
                throw new InvalidOperationException($"Extension '{id}' declares no updateFeed.");

            ExtensionUpdateInfo latest = await ExtensionUpdateFeed.ResolveAsync(installed.Manifest.UpdateFeed!, ct);
            string zipPath = await ExtensionUpdateFeed.DownloadPackageAsync(latest.DownloadUrl, installed.Manifest.Id, ct);

            // The feed must serve the SAME extension it was declared for — checked BEFORE anything
            // is installed, so a wrong or hijacked feed cannot plant a different package.
            ExtensionPackageInfo packageInfo = ExtensionPackage.Validate(zipPath);
            if (!string.Equals(packageInfo.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The update feed of '{id}' served a package with id '{packageInfo.Manifest.Id}' — " +
                    "refusing to install it. Check the vendor's feed.");

            ExtensionInstallResult result = ExtensionInstaller.InstallPackage(
                zipPath, overwrite: true, CoreServices.RevitVersion);

            Log.Information("Updated extension {Id}: {From} -> {To} (feed: {Feed})",
                id, installed.Manifest.Version, result.Info.Manifest.Version, installed.Manifest.UpdateFeed);

            CoreServices.ReloadExtensions();

            return new
            {
                updated = true,
                id,
                from = installed.Manifest.Version,
                to = result.Info.Manifest.Version,
            };
        }

        internal sealed record Request
        {
            [Description("Extension id (as reported by GetInstalledExtensions; managed zone only).")]
            public string Id { get; set; } = string.Empty;
        }
    }
}
