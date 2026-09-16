using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using Serilog;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>One required extension after an apply pass: what was done, or why not.</summary>
    internal sealed record RequiredExtensionOutcome(string Id, string State, string? Version, string? Detail);

    /// <summary>
    /// <c>extensions.required</c> (design §2): extensions every seat must have. Installed when missing,
    /// updated when the feed is newer, never disabled or removed by the user. Runs in the background
    /// after startup (<see cref="PolicyBackgroundApply"/>), never on the Revit startup path.
    /// </summary>
    internal static class RequiredExtensions
    {
        private static readonly object Gate = new();
        private static IReadOnlyList<RequiredExtensionOutcome> _lastOutcomes = Array.Empty<RequiredExtensionOutcome>();
        private static DateTimeOffset? _lastRun;

        public static IReadOnlyList<RequiredExtension> Declared =>
            PolicyStore.Current.IsPresent
                ? (PolicyStore.Current.Document.Extensions?.Required ?? new List<RequiredExtension>())
                    .Where(r => !string.IsNullOrWhiteSpace(r.Id)).ToList()
                : Array.Empty<RequiredExtension>();

        public static bool IsRequired(string extensionId) =>
            Declared.Any(r => string.Equals(r.Id, extensionId, StringComparison.OrdinalIgnoreCase));

        public static RequiredExtension? Find(string extensionId) =>
            Declared.FirstOrDefault(r => string.Equals(r.Id, extensionId, StringComparison.OrdinalIgnoreCase));

        public static string RefusalFor(string extensionId, string action)
        {
            string who = PolicyStore.Current.Document.Organization?.Name ?? "your organization";
            return $"'{extensionId}' is required by {who} ({PolicyStore.Current.Path}) and cannot be {action} here.";
        }

        /// <summary>What the last background pass did, for the Organization panel.</summary>
        public static (DateTimeOffset? LastRun, IReadOnlyList<RequiredExtensionOutcome> Outcomes) LastReport()
        {
            lock (Gate) return (_lastRun, _lastOutcomes);
        }

        /// <summary>Installs missing and updates outdated required extensions. Returns true when
        /// anything on disk changed (the caller reloads). Every failure is an outcome, never an exception.</summary>
        public static async Task<bool> ApplyAsync(CancellationToken ct)
        {
            IReadOnlyList<RequiredExtension> declared = Declared;
            List<RequiredExtensionOutcome> outcomes = new();
            bool changed = false;

            if (declared.Count > 0)
            {
                string revitVersion = CoreServices.RevitVersion;
                Dictionary<string, ExtensionDescriptor> installed = ExtensionCatalog.EnumerateAll(revitVersion)
                    .GroupBy(d => d.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (RequiredExtension required in declared)
                {
                    ct.ThrowIfCancellationRequested();
                    installed.TryGetValue(required.Id, out ExtensionDescriptor? current);
                    RequiredExtensionOutcome outcome = await ApplyOneAsync(required, current, revitVersion, ct);
                    outcomes.Add(outcome);
                    if (outcome.State is "installed" or "updated") changed = true;
                }
            }

            lock (Gate)
            {
                _lastOutcomes = outcomes;
                _lastRun = DateTimeOffset.Now;
            }
            return changed;
        }

        private static async Task<RequiredExtensionOutcome> ApplyOneAsync(
            RequiredExtension required, ExtensionDescriptor? current, string revitVersion, CancellationToken ct)
        {
            string id = required.Id.Trim();
            try
            {
                // A dev-zone or machine-zone copy satisfies the requirement: it is the same id, and the
                // installer would refuse a managed twin anyway.
                if (current is not null && current.Zone != ExtensionZone.Managed)
                    return new RequiredExtensionOutcome(id, "present", current.Manifest.Version,
                        $"present as a {current.Zone.ToString().ToLowerInvariant()} extension");

                string? feed = string.IsNullOrWhiteSpace(required.Source) ? current?.Manifest.UpdateFeed : required.Source;
                if (string.IsNullOrWhiteSpace(feed))
                    return new RequiredExtensionOutcome(id, current is null ? "missing" : "present", current?.Manifest.Version,
                        current is null ? "no source in the policy and nothing installed" : "no source to update from");

                feed = ExtensionUpdateFeed.Normalize(feed!);
                if (PolicyFeedRules.Refusal(feed) is string refusedFeed)
                    return new RequiredExtensionOutcome(id, "blocked", current?.Manifest.Version, refusedFeed);

                ExtensionUpdateInfo latest = await ExtensionUpdateFeed.ResolveAsync(feed, id, ct);
                if (current is not null && !Features.Extensions.CheckExtensionUpdates.IsNewer(latest.Version, current.Manifest.Version))
                    return new RequiredExtensionOutcome(id, "present", current.Manifest.Version, null);

                if (PolicyFeedRules.DownloadRefusal(feed, latest.DownloadUrl) is string refusedDownload)
                    return new RequiredExtensionOutcome(id, "blocked", current?.Manifest.Version, refusedDownload);

                string zipPath = await ExtensionUpdateFeed.DownloadPackageAsync(latest.DownloadUrl, id, ct);
                if (PackageHash.Verify(zipPath, required.Sha256, id) is string badHash)
                    return new RequiredExtensionOutcome(id, "blocked", current?.Manifest.Version, badHash);

                ExtensionPackageInfo probe = ExtensionPackage.Validate(zipPath);
                if (!string.Equals(probe.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
                    return new RequiredExtensionOutcome(id, "blocked", current?.Manifest.Version,
                        $"the source serves '{probe.Manifest.Id}', not '{id}'");

                ExtensionInstallResult result = ExtensionInstaller.InstallPackage(zipPath, overwrite: current is not null, revitVersion);
                Log.Information("Required extension {Id} {State}: {Version} from {Feed}",
                    id, current is null ? "installed" : "updated", result.Info.Manifest.Version, feed);
                return new RequiredExtensionOutcome(id, current is null ? "installed" : "updated", result.Info.Manifest.Version, feed);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Required extension {Id} could not be applied", id);
                return new RequiredExtensionOutcome(id, "failed", current?.Manifest.Version, ex.Message);
            }
        }
    }
}
