using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Core.Common.Policy;
using AnalyseTool.Core.Common.Telemetry;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Policy
{
    /// <summary>
    /// What the organization policy does to this seat, for the Settings "Organization" panel and the
    /// CLI: whether a file was found, what it could not read, which settings it locks and what each
    /// managed setting resolves to and from where. Read-only; nothing here touches the Revit model.
    /// </summary>
    [RevitCommand(
        Description = "Reports the organization policy applied to this installation: whether a policy " +
                      "file exists, its problems, the locked settings and the effective value of each " +
                      "managed setting with its origin.",
        ReadOnly = true,
        HiddenFromMcp = true)] // plugin self-management, not for the AI
    internal sealed class GetPolicyStatus : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            PolicyState policy = PolicyStore.Current;
            PolicyDocument doc = policy.Document;
            (DateTimeOffset? lastRun, IReadOnlyList<RequiredExtensionOutcome> outcomes) = RequiredExtensions.LastReport();
            (bool applying, DateTimeOffset? lastCompleted, string? applyError) = PolicyBackgroundApply.Status();

            return Task.FromResult<object?>(new
            {
                present = policy.IsPresent,
                origin = policy.Origin.ToString().ToLowerInvariant(), // absent | loaded | invalid
                path = policy.Path,
                problems = policy.Problems,
                organization = doc.Organization is null ? null : new
                {
                    name = doc.Organization.Name,
                    contact = doc.Organization.Contact,
                },
                minimumVersion = doc.MinimumVersion,
                pluginVersion = SharedData.ToolData.PLUGIN_VERSION,
                // Pointer form: recognized, followed from phase 3a on.
                policyUrl = doc.PolicyUrl,
                locked = doc.Locked,
                settings = new
                {
                    codeExecution = new
                    {
                        enabled = CodeExecutionSettings.Enabled,
                        origin = CodeExecutionSettings.Origin,
                        locked = CodeExecutionSettings.IsManaged,
                    },
                    extensionRoots = new
                    {
                        fromPolicy = ExtensionSources.PolicyRoots(),
                        locked = ExtensionSources.RootsLocked,
                    },
                    allowedFeeds = doc.Extensions?.AllowedFeeds,
                    allowInstallFromRepository = PolicyFeedRules.InstallFromRepositoryAllowed,
                    mcpEnabled = new
                    {
                        value = doc.Mcp?.Enabled,
                        locked = policy.IsLocked(PolicySettings.McpEnabled),
                    },
                },
                machineExtensionsRoot = PathProvider.MachineExtensionsDistRoot,
                belowMinimumVersion = IsBelow(SharedData.ToolData.PLUGIN_VERSION, doc.MinimumVersion),
                update = doc.Update is null ? null : new { doc.Update.DownloadUrl, pinned = doc.Update.Sha256 is not null },
                // The organization layer: joined by the user, or set by the machine pointer.
                membership = policy.Membership is null ? null : new
                {
                    policyUrl = policy.Membership.PolicyUrl,
                    enforced = policy.PointerEnforced,
                    organization = policy.Membership.OrganizationName,
                    fetchedAt = policy.Membership.FetchedAt,
                    applied = policy.Organization is not null,
                    signed = policy.Membership.SigningKey is not null,
                    signingKeyFingerprint = PolicySignature.Fingerprint(policy.Membership.SigningKey),
                    lastRefreshProblem = PolicyStore.OrgRefreshProblem,
                },
                layers = new
                {
                    machine = policy.Machine.CodeExecution is not null || policy.Machine.Extensions is not null || policy.Machine.Mcp is not null || policy.Machine.IsPointer,
                    organization = policy.Organization is not null,
                    codeExecution = policy.LayerOf("codeExecution"),
                    extensions = policy.LayerOf("extensions"),
                    mcp = policy.LayerOf("mcp"),
                },
                sources = new
                {
                    declared = doc.Sources?.Select(kv => new
                    {
                        name = kv.Key,
                        kind = kv.Value.Url is not null ? "url" : kv.Value.Path is not null ? "path" : "sharepoint",
                        sharepoint = kv.Value.Sharepoint,
                        syncUrl = kv.Value.SyncUrl,
                        resolved = PolicySourceResolver.Resolve("source:" + kv.Key, out _),
                    }),
                    unresolved = PolicySourceResolver.UnresolvedSources(),
                    overrides = PolicySourceResolver.CurrentOverrides(),
                },
                catalog = new
                {
                    source = ExtensionSourceCatalog.PolicyCatalogSource,
                },
                required = new
                {
                    declared = RequiredExtensions.Declared.Select(r => new { id = r.Id, source = r.Source, pinned = r.Sha256 is not null }),
                    lastRun,
                    outcomes = outcomes.Select(o => new { o.Id, state = o.State, o.Version, o.Detail }),
                },
                backgroundApply = new { running = applying, lastCompleted, error = applyError },
                telemetry = TelemetryHub.Configuration is { } tp
                    ? new { enabled = true, sink = tp.Sink, identity = tp.Identity ?? "hashed", events = tp.Events ?? new List<string> { "inventory" }, pending = TelemetryHub.Status().Pending, lastError = TelemetryHub.Status().LastError }
                    : new { enabled = false, sink = (string?)null, identity = "hashed", events = new List<string>(), pending = 0, lastError = (string?)null },
                logging = doc.Logging?.ToString(Newtonsoft.Json.Formatting.None),
            });
        }

        /// <summary>True when the plugin is older than the organization's minimum. Unparseable = false.</summary>
        internal static bool IsBelow(string plugin, string? minimum) =>
            !string.IsNullOrWhiteSpace(minimum)
            && Version.TryParse(plugin, out Version? p) && Version.TryParse(minimum, out Version? m)
            && p < m;
    }

    /// <summary>Exactly what left this seat lately (design §10, "Show recent events"): the last
    /// telemetry lines as sent. Empty when telemetry is off.</summary>
    [RevitCommand(
        Description = "Returns the most recent telemetry events this installation sent to the organization's sink.",
        ReadOnly = true,
        HiddenFromMcp = true)]
    internal sealed class GetTelemetryRecent : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(new
            {
                enabled = TelemetryHub.Configuration is not null,
                events = TelemetryHub.RecentEvents(),
            });
    }

    /// <summary>Re-reads the policy file. For a Settings "Reload" button and for the CLI; an edited
    /// file otherwise applies on the next Revit start.</summary>
    [RevitCommand(
        Description = "Re-reads the organization policy file and reloads extensions so new roots apply.",
        HiddenFromMcp = true)]
    internal sealed class ReloadPolicy : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            PolicyStore.Reload();
            CoreServices.ReloadExtensions();
            PolicyBackgroundApply.Start(TimeSpan.Zero); // catalog + required extensions, off this thread
            PolicyState policy = PolicyStore.Current;
            return Task.FromResult<object?>(new
            {
                present = policy.IsPresent,
                origin = policy.Origin.ToString().ToLowerInvariant(),
                problems = policy.Problems,
            });
        }
    }
}
