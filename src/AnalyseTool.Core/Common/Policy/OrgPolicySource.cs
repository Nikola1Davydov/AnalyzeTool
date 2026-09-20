using Newtonsoft.Json;
using Serilog;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>What fetching an organization policy produced: the parsed document, its raw text
    /// (what a signature covers), where it came from, how it was verified, and a problem if any.</summary>
    internal sealed record OrgPolicyFetch(
        PolicyDocument? Document,
        string? Text,
        string? Location,
        string? Signature,
        SignatureState State,
        bool FromCache,
        string? Problem)
    {
        public bool Accepted => Document is not null && Problem is null;
    }

    /// <summary>
    /// Fetches and verifies an organization policy (Join, the background refresh, the machine
    /// pointer). One rule set for all three: https or a path (or a <c>source:</c> reference), plain
    /// http refused, a detached signature required whenever a key is known, and a redirect to another
    /// host reported instead of followed silently.
    /// </summary>
    internal static class OrgPolicySource
    {
        /// <summary>Fetches the policy at <paramref name="reference"/>. <paramref name="expectedHost"/>
        /// (from a previous fetch) turns a host change into a problem.</summary>
        public static async Task<OrgPolicyFetch> FetchAsync(
            string reference, string? signingKey, string? expectedHost, CancellationToken ct)
        {
            string? resolved = PolicySourceResolver.Resolve(reference, out string? resolveProblem);
            if (resolved is null)
                return new OrgPolicyFetch(null, null, null, null, SignatureState.NoKey, false, resolveProblem);

            (PolicySourceContent? content, string? problem) = await PolicySourceReader.ReadAsync(resolved, ct);
            if (content is null)
                return new OrgPolicyFetch(null, null, resolved, null, SignatureState.NoKey, false, problem ?? "Nothing was read.");

            if (expectedHost is not null && HostOf(content.Location) is string host
                && !string.Equals(host, expectedHost, StringComparison.OrdinalIgnoreCase))
                return new OrgPolicyFetch(null, content.Text, content.Location, null, SignatureState.NoKey, content.FromCache,
                    $"The policy now comes from '{host}' instead of '{expectedHost}'. Leave and join the new location deliberately.");

            string? signature = null;
            SignatureState state = SignatureState.NoKey;
            if (!string.IsNullOrWhiteSpace(signingKey))
            {
                (PolicySourceContent? sig, _) = await PolicySourceReader.ReadAsync(PolicySignature.SignaturePathFor(resolved), ct);
                signature = sig?.Text;
                state = PolicySignature.Verify(signingKey, PolicySignature.Utf8(content.Text), signature);
                if (state != SignatureState.Verified)
                    return new OrgPolicyFetch(null, content.Text, content.Location, signature, state, content.FromCache,
                        state == SignatureState.Missing
                            ? "The policy is not signed, but this seat knows the organization's signing key. Refusing it."
                            : "The policy's signature does not match the organization's signing key. Refusing it.");
            }

            PolicyDocument? document;
            try
            {
                document = JsonConvert.DeserializeObject<PolicyDocument>(content.Text);
            }
            catch (Exception ex)
            {
                return new OrgPolicyFetch(null, content.Text, content.Location, signature, state, content.FromCache,
                    $"The policy could not be parsed: {ex.Message}");
            }
            if (document is null)
                return new OrgPolicyFetch(null, content.Text, content.Location, signature, state, content.FromCache, "The policy is empty.");
            document.Locked ??= new List<string>();

            if (document.IsPointer)
                return new OrgPolicyFetch(null, content.Text, content.Location, signature, state, content.FromCache,
                    "The policy at this location is itself a pointer. Pointers may not chain.");
            if (string.IsNullOrWhiteSpace(document.Organization?.Name) || string.IsNullOrWhiteSpace(document.Organization?.Contact))
                return new OrgPolicyFetch(document, content.Text, content.Location, signature, state, content.FromCache,
                    "A joinable policy must name its organization (organization.name) and a contact (organization.contact).");

            return new OrgPolicyFetch(document, content.Text, content.Location, signature, state, content.FromCache, problem);
        }

        public static string? HostOf(string? location) =>
            location is not null && Uri.TryCreate(location, UriKind.Absolute, out Uri? u) && u.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? u.Host
                : null;

        /// <summary>The background half: re-fetch the joined policy, accept a changed one, keep the cache
        /// on failure. Returns true when the effective policy changed (the caller reloads).</summary>
        public static async Task<bool> RefreshAsync(CancellationToken ct)
        {
            OrgMembership? membership = OrgMembershipStore.Load();
            if (membership is null) return false;

            OrgPolicyFetch fetch = await FetchAsync(membership.PolicyUrl, membership.SigningKey,
                HostOf(membership.LastLocation), ct);
            PolicyStore.SetOrgRefreshProblem(fetch.Problem);
            if (!fetch.Accepted) return false;

            bool changed = !string.Equals(fetch.Text, membership.CachedPolicy, StringComparison.Ordinal);
            membership.CachedPolicy = fetch.Text;
            membership.LastLocation = fetch.Location;
            membership.LastSignature = fetch.Signature;
            membership.FetchedAt = DateTimeOffset.Now;
            membership.OrganizationName = fetch.Document!.Organization?.Name;
            try { OrgMembershipStore.Save(membership); }
            catch (Exception ex) { Log.Warning(ex, "Could not save org.json after refresh"); }

            if (changed)
            {
                Log.Information("Organization policy changed at {Location}; applying", fetch.Location);
                PolicyStore.Reload();
            }
            return changed;
        }
    }
}
