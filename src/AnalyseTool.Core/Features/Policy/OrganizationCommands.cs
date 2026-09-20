using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Policy;
using AnalyseTool.Sdk;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Policy
{
    /// <summary>
    /// Finds an organization policy from what the user typed (a URL, a domain, a folder, a
    /// <c>source:</c> reference, or nothing) and returns a PREVIEW of what joining would do — the
    /// consent step of design §8. Nothing is applied here.
    /// </summary>
    [RevitCommand(
        Description = "Finds an organization policy (from a URL, domain, folder or automatically) and " +
                      "previews what joining it would change. Applies nothing.",
        InputType = typeof(DiscoverOrganizationPolicy.Request),
        ReadOnly = true,
        HiddenFromMcp = true)]
    internal sealed class DiscoverOrganizationPolicy : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            IReadOnlyList<PolicyCandidate> candidates = PolicyDiscovery.Candidates(req.Input);
            List<object> tried = new();

            foreach (PolicyCandidate candidate in candidates)
            {
                OrgPolicyFetch fetch = await OrgPolicySource.FetchAsync(candidate.Reference, req.SigningKey, null, ct);
                tried.Add(new { candidate.Reference, candidate.How, problem = fetch.Problem });
                if (fetch.Accepted)
                    return new { found = true, tried, preview = Preview(candidate, fetch, req.SigningKey) };
            }

            return new { found = false, tried, preview = (object?)null };
        }

        /// <summary>Everything consequential a policy would do to this seat, named explicitly.</summary>
        internal static object Preview(PolicyCandidate candidate, OrgPolicyFetch fetch, string? signingKey)
        {
            PolicyDocument doc = fetch.Document!;
            PolicyState now = PolicyStore.Current;
            return new
            {
                reference = candidate.Reference,
                location = fetch.Location,
                how = candidate.How,
                organization = new { name = doc.Organization?.Name, contact = doc.Organization?.Contact },
                signature = fetch.State.ToString().ToLowerInvariant(), // nokey | verified
                signingKeyFingerprint = PolicySignature.Fingerprint(signingKey),
                locks = doc.Locked,
                codeExecution = doc.CodeExecution?.Enabled,
                mcpEnabled = doc.Mcp?.Enabled,
                extensionRoots = doc.Extensions?.Roots ?? new List<string>(),
                catalogUrl = doc.Extensions?.CatalogUrl,
                allowedFeeds = doc.Extensions?.AllowedFeeds,
                allowInstallFromRepository = doc.Extensions?.AllowInstallFromRepository,
                requiredExtensions = (doc.Extensions?.Required ?? new List<RequiredExtension>())
                    .Select(r => new { r.Id, r.Source, pinned = r.Sha256 is not null }),
                sources = doc.Sources?.Keys ?? Enumerable.Empty<string>(),
                minimumVersion = doc.MinimumVersion,
                // Sections read by later phases; shown raw so the preview never hides a consequence.
                aiSection = doc.Ai?.ToString(Newtonsoft.Json.Formatting.None),
                telemetrySection = doc.Telemetry?.ToString(Newtonsoft.Json.Formatting.None),
                loggingSection = doc.Logging?.ToString(Newtonsoft.Json.Formatting.None),
                alreadyJoined = now.Membership is not null,
                machinePolicyPresent = now.Machine.CodeExecution is not null || now.Machine.Extensions is not null || now.Machine.Mcp is not null,
            };
        }

        internal sealed record Request
        {
            [Description("A policy URL (https), a company domain, a folder path, a 'source:' reference — or empty to discover automatically.")]
            public string? Input { get; set; }

            [Description("Optional base64 signing key handed out by the organization; when given, the policy must be signed with it.")]
            public string? SigningKey { get; set; }
        }
    }

    /// <summary>Joins an organization: fetches and verifies the policy once more, records the
    /// membership in <c>org.json</c>, applies it, and starts the background pass (catalog, required
    /// extensions). The preview from <see cref="DiscoverOrganizationPolicy"/> is the consent.</summary>
    [RevitCommand(
        Description = "Joins an organization's configuration: records the policy location, applies the policy " +
                      "and installs required extensions in the background.",
        InputType = typeof(JoinOrganization.Request),
        Destructive = true,
        HiddenFromMcp = true)]
    internal sealed class JoinOrganization : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(req?.Reference))
                throw new InvalidOperationException("The policy location is required.");
            if (!req.Consent)
                throw new InvalidOperationException("Joining requires the user to confirm the preview.");
            if (PolicyStore.Current.PointerEnforced)
                throw new InvalidOperationException("This computer's organization is set by an administrator and cannot be changed here.");

            OrgPolicyFetch fetch = await OrgPolicySource.FetchAsync(req.Reference.Trim(), req.SigningKey, null, ct);
            if (!fetch.Accepted)
                throw new InvalidOperationException(fetch.Problem ?? "The policy could not be read.");

            OrgMembershipStore.Save(new OrgMembership
            {
                PolicyUrl = req.Reference.Trim(),
                SigningKey = string.IsNullOrWhiteSpace(req.SigningKey) ? null : req.SigningKey.Trim(),
                OrganizationName = fetch.Document!.Organization?.Name,
                FetchedAt = DateTimeOffset.Now,
                CachedPolicy = fetch.Text,
                LastLocation = fetch.Location,
                LastSignature = fetch.Signature,
            });

            PolicyStore.Reload();
            CoreServices.ReloadExtensions();
            PolicyBackgroundApply.Start(TimeSpan.Zero);

            return new
            {
                joined = true,
                organization = fetch.Document.Organization?.Name,
                signature = fetch.State.ToString().ToLowerInvariant(),
            };
        }

        internal sealed record Request
        {
            [Description("The policy location exactly as the preview reported it (URL, path or source: reference).")]
            public string Reference { get; set; } = string.Empty;

            [Description("Optional base64 signing key; stored with the membership and required on every refresh.")]
            public string? SigningKey { get; set; }

            [Description("Must be true: the user has seen the preview and confirmed.")]
            public bool Consent { get; set; }
        }
    }

    /// <summary>Leaves the organization: deletes <c>org.json</c>, releases the locks, and reports the
    /// extensions that were installed because the policy required them — offered for removal, never
    /// removed here. The user's own files are untouched, so their pre-join settings come back.</summary>
    [RevitCommand(
        Description = "Leaves the organization's configuration; reports required extensions that can now be removed.",
        Destructive = true,
        HiddenFromMcp = true)]
    internal sealed class LeaveOrganization : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            PolicyState before = PolicyStore.Current;
            if (before.PointerEnforced)
                throw new InvalidOperationException("This computer's organization is set by an administrator and cannot be left here.");
            if (before.Membership is null)
                return Task.FromResult<object?>(new { left = false, reason = "This seat is not joined to an organization." });

            List<string> wereRequired = RequiredExtensions.Declared.Select(r => r.Id).ToList();
            string? name = before.Membership.OrganizationName;

            OrgMembershipStore.Delete();
            PolicyStore.Reload();
            CoreServices.ReloadExtensions();

            // Installed because of the policy and still present in the managed zone → the user may
            // now uninstall them. Listed, not removed: leaving must not silently delete tools.
            HashSet<string> installed = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                .Where(d => d.Zone == ExtensionZone.Managed)
                .Select(d => d.Manifest.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Task.FromResult<object?>(new
            {
                left = true,
                organization = name,
                removableExtensions = wereRequired.Where(installed.Contains).ToList(),
            });
        }
    }

    /// <summary>The user's answer to "where is this source on this computer?" — stored per seat.</summary>
    [RevitCommand(
        Description = "Tells the plugin where a policy-declared source folder lives on this computer (or forgets it).",
        InputType = typeof(SetSourceLocation.Request),
        HiddenFromMcp = true)]
    internal sealed class SetSourceLocation : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(req?.Name))
                throw new InvalidOperationException("The source name is required.");
            if (!string.IsNullOrWhiteSpace(req.Path) && !Directory.Exists(req.Path))
                throw new InvalidOperationException($"'{req.Path}' is not a folder.");

            PolicySourceResolver.SetOverride(req.Name.Trim(), req.Path);
            CoreServices.ReloadExtensions();
            PolicyBackgroundApply.Start(TimeSpan.Zero);
            return Task.FromResult<object?>(new { name = req.Name.Trim(), path = req.Path, unresolved = PolicySourceResolver.UnresolvedSources() });
        }

        internal sealed record Request
        {
            [Description("The source name as declared in the policy's 'sources'.")]
            public string Name { get; set; } = string.Empty;

            [Description("The folder on this computer; empty to forget the stored answer.")]
            public string? Path { get; set; }
        }
    }
}
