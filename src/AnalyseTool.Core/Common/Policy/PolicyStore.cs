using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>How the policy file was found.</summary>
    internal enum PolicyOrigin
    {
        /// <summary>No file: the tool is the single-seat product it has always been.</summary>
        Absent,

        /// <summary>Read and parsed (possibly with warnings in <see cref="PolicyState.Problems"/>).</summary>
        Loaded,

        /// <summary>The file exists but could not be read or parsed. Treated as absent, reported.</summary>
        Invalid,
    }

    /// <summary>One loaded policy: the document (empty when absent or invalid), where it came from,
    /// and everything the loader wants a human to know about it.</summary>
    internal sealed record PolicyState(
        PolicyDocument Document,
        string Path,
        PolicyOrigin Origin,
        IReadOnlyList<string> Problems)
    {
        public bool IsPresent => Origin == PolicyOrigin.Loaded;

        /// <summary>The machine file as read (inline form), or empty. <see cref="Document"/> is the merge.</summary>
        public PolicyDocument Machine { get; init; } = new();

        /// <summary>The organization policy the seat joined (or the machine pointer targets), or null.</summary>
        public PolicyDocument? Organization { get; init; }

        /// <summary>The membership record behind <see cref="Organization"/>, or null.</summary>
        public OrgMembership? Membership { get; init; }

        /// <summary>The machine file is a pointer: the organization layer is mandatory and Leave is refused.</summary>
        public bool PointerEnforced { get; init; }

        /// <summary>Which layer supplies a top-level section: "machine", "organization" or null.</summary>
        public string? LayerOf(string section)
        {
            if (Has(Machine, section)) return "machine";
            if (Organization is not null && Has(Organization, section)) return "organization";
            return null;

            static bool Has(PolicyDocument d, string section) => section switch
            {
                "codeExecution" => d.CodeExecution is not null,
                "extensions" => d.Extensions is not null,
                "mcp" => d.Mcp is not null,
                "sources" => d.Sources is not null,
                _ => false,
            };
        }

        /// <summary>The user may not change this setting: it is present in the policy AND listed under
        /// <c>locked</c>. A present-but-unlocked value is only a default.</summary>
        public bool IsLocked(string setting) =>
            IsPresent && Document.Locked.Any(l => string.Equals(l, setting, StringComparison.OrdinalIgnoreCase));

        /// <summary>Resolves one scalar setting across the layers:
        /// locked policy value → the user's own value → unlocked policy value (a default) → fallback.</summary>
        public T Resolve<T>(string setting, T? policyValue, T? userValue, T fallback) where T : struct
        {
            if (policyValue.HasValue && IsLocked(setting)) return policyValue.Value;
            if (userValue.HasValue) return userValue.Value;
            if (policyValue.HasValue) return policyValue.Value;
            return fallback;
        }

        /// <summary>Which layer <see cref="Resolve{T}"/> would answer from — for the UI's "origin" column.</summary>
        public string OriginOf<T>(string setting, T? policyValue, T? userValue) where T : struct
        {
            if (policyValue.HasValue && IsLocked(setting)) return "policy";
            if (userValue.HasValue) return "user";
            if (policyValue.HasValue) return "policy-default";
            return "default";
        }

        /// <summary>The refusal a store returns when a locked setting is written.</summary>
        public string LockedMessage(string setting)
        {
            string who = string.IsNullOrWhiteSpace(Document.Organization?.Name)
                ? "your organization"
                : Document.Organization!.Name!;
            return $"'{setting}' is managed by {who} ({Path}) and cannot be changed here.";
        }

        public static PolicyState Absent(string path) =>
            new(new PolicyDocument(), path, PolicyOrigin.Absent, Array.Empty<string>());
    }

    /// <summary>
    /// Loads the machine-layer policy once per process and hands the result to every store that has
    /// a setting a policy can shape. Headless: a missing or broken file is logged and reported through
    /// <see cref="PolicyState.Problems"/>, never a dialog and never an exception to the caller.
    /// <para>
    /// The plugin NEVER writes the machine file. Phase 1 reads exactly one source,
    /// <see cref="PathProvider.PolicyPath"/>; the organization layer (a policy the user joined by URL)
    /// arrives in phase 3b and merges here.
    /// </para>
    /// </summary>
    internal static class PolicyStore
    {
        private static readonly object Gate = new();
        private static PolicyState? _current;
        private static string? _pathOverride;

        private static string? _orgRefreshProblem;

        /// <summary>The effective policy: the machine file merged with the organization layer. Loaded
        /// on first access from local files only; call <see cref="Reload"/> after either changed.</summary>
        public static PolicyState Current
        {
            get
            {
                lock (Gate)
                    return _current ??= LoadLayered(_pathOverride ?? PathProvider.PolicyPath, _membershipOverride);
            }
        }

        /// <summary>What the last background refresh of the organization policy reported, if anything.</summary>
        public static string? OrgRefreshProblem { get { lock (Gate) return _orgRefreshProblem; } }
        public static void SetOrgRefreshProblem(string? problem) { lock (Gate) _orgRefreshProblem = problem; }

        private static Func<OrgMembership?>? _membershipOverride;

        /// <summary>Tests only: supply the membership instead of reading org.json.</summary>
        internal static void OverrideMembershipForTests(Func<OrgMembership?>? membership)
        {
            lock (Gate) { _membershipOverride = membership; _current = null; }
        }

        /// <summary>Machine file + organization layer → one state. Local disk only (the organization
        /// policy comes from the org.json cache; the background refresh keeps that cache fresh).</summary>
        public static PolicyState LoadLayered(string machinePath, Func<OrgMembership?>? membershipSource = null)
        {
            PolicyState machine = Load(machinePath);
            List<string> problems = new(machine.Problems);

            OrgMembership? membership = null;
            bool pointerEnforced = false;
            if (machine.IsPresent && machine.Document.IsPointer)
            {
                // The pointer IS the membership: mandatory when enforced, and never written by the plugin.
                pointerEnforced = machine.Document.Enforced == true;
                OrgMembership? stored = (membershipSource ?? OrgMembershipStore.Load)();
                membership = new OrgMembership
                {
                    PolicyUrl = machine.Document.PolicyUrl!.Trim(),
                    Enforced = pointerEnforced,
                    SigningKey = machine.Document.SigningKey,
                    CachedPolicy = SameTarget(stored, machine.Document.PolicyUrl!) ? stored!.CachedPolicy : null,
                    LastLocation = SameTarget(stored, machine.Document.PolicyUrl!) ? stored!.LastLocation : null,
                    LastSignature = SameTarget(stored, machine.Document.PolicyUrl!) ? stored!.LastSignature : null,
                    FetchedAt = SameTarget(stored, machine.Document.PolicyUrl!) ? stored!.FetchedAt : null,
                    OrganizationName = SameTarget(stored, machine.Document.PolicyUrl!) ? stored!.OrganizationName : null,
                };
                problems.RemoveAll(p => p.Contains("policyUrl", StringComparison.Ordinal)); // followed now
            }
            else
            {
                membership = (membershipSource ?? OrgMembershipStore.Load)();
            }

            PolicyDocument? org = null;
            if (membership is not null)
            {
                if (!membership.HasCachedPolicy)
                    problems.Add($"Joined '{membership.PolicyUrl}' but its policy has not been fetched yet; it applies once the background refresh succeeds.");
                else if (!string.IsNullOrWhiteSpace(membership.SigningKey)
                         && PolicySignature.Verify(membership.SigningKey, PolicySignature.Utf8(membership.CachedPolicy!), membership.LastSignature) != SignatureState.Verified)
                    // org.json is the user's own file: a cached copy is trusted only as far as the key
                    // (from the pointer, or recorded at Join) still vouches for it. Edited = not applied.
                    problems.Add("The cached organization policy does not verify against the organization's signing key; it is not applied until a fresh signed copy is fetched.");
                else
                {
                    try
                    {
                        org = JsonConvert.DeserializeObject<PolicyDocument>(membership.CachedPolicy!);
                        if (org is not null) org.Locked ??= new List<string>();
                    }
                    catch (Exception ex)
                    {
                        problems.Add($"The cached organization policy could not be parsed: {ex.Message}");
                    }
                }
            }

            PolicyDocument merged = Merge(machine.IsPresent ? machine.Document : new PolicyDocument(), org);
            bool present = machine.IsPresent || org is not null;
            return new PolicyState(merged, machine.Path, present ? PolicyOrigin.Loaded : machine.Origin, problems)
            {
                Machine = machine.IsPresent ? machine.Document : new PolicyDocument(),
                Organization = org,
                Membership = membership,
                PointerEnforced = pointerEnforced,
            };

            static bool SameTarget(OrgMembership? stored, string url) =>
                stored is not null && string.Equals(stored.PolicyUrl, url.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Section-wise: the machine layer supplies a section when it has it, else the
        /// organization. A setting is locked when the layer that supplies its section locks it.
        /// Pure — tested directly.</summary>
        internal static PolicyDocument Merge(PolicyDocument machine, PolicyDocument? org)
        {
            if (org is null) return machine;

            PolicyDocument merged = new()
            {
                Version = 1,
                Organization = machine.Organization ?? org.Organization,
                Revision = machine.Revision ?? org.Revision,
                MinimumVersion = machine.MinimumVersion ?? org.MinimumVersion,
                Update = machine.Update ?? org.Update,
                CodeExecution = machine.CodeExecution ?? org.CodeExecution,
                Extensions = machine.Extensions ?? org.Extensions,
                Mcp = machine.Mcp ?? org.Mcp,
                Sources = machine.Sources ?? org.Sources,
                Ai = machine.Ai ?? org.Ai,
                Logging = machine.Logging ?? org.Logging,
                Telemetry = machine.Telemetry ?? org.Telemetry,
                Locked = new List<string>(),
            };

            foreach (string setting in PolicySettings.All)
            {
                string section = setting[..setting.IndexOf('.')];
                bool fromMachine = section switch
                {
                    "codeExecution" => machine.CodeExecution is not null,
                    "extensions" => machine.Extensions is not null,
                    "mcp" => machine.Mcp is not null,
                    _ => false,
                };
                List<string> locks = fromMachine ? machine.Locked : org.Locked;
                if (locks.Any(l => string.Equals(l, setting, StringComparison.OrdinalIgnoreCase)))
                    merged.Locked.Add(setting);
            }
            return merged;
        }

        /// <summary>Drops the cached state so the next read goes back to disk.</summary>
        public static void Reload()
        {
            lock (Gate) _current = null;
        }

        /// <summary>Tests only: read the policy from another file (null restores the default path).
        /// Takes effect on the next read.</summary>
        internal static void OverridePathForTests(string? path)
        {
            lock (Gate)
            {
                _pathOverride = path;
                _current = null;
            }
        }

        /// <summary>Reads and validates one policy FILE (the machine layer). Pure: no cache, no global
        /// state — the unit the tier-1 tests exercise.</summary>
        public static PolicyState Load(string path)
        {
            if (!File.Exists(path))
                return PolicyState.Absent(path);

            List<string> problems = new();
            PolicyDocument document;
            try
            {
                document = JsonConvert.DeserializeObject<PolicyDocument>(File.ReadAllText(path)) ?? new PolicyDocument();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read the organization policy at {Path}", path);
                return new PolicyState(new PolicyDocument(), path, PolicyOrigin.Invalid,
                    new[] { $"Could not read {path}: {ex.Message}. The policy is ignored." });
            }

            // "locked": null in the file must not become a null list downstream.
            document.Locked ??= new List<string>();

            if (document.Version != 1)
                problems.Add($"Unsupported policy version {document.Version}; this plugin reads version 1. " +
                             "Known sections are applied, the rest is ignored.");

            if (document.Unknown is { Count: > 0 })
                problems.Add("Unknown keys ignored: " + string.Join(", ", document.Unknown.Keys.OrderBy(k => k)));

            foreach (string locked in document.Locked)
                if (!PolicySettings.All.Any(s => string.Equals(s, locked, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"'locked' names an unknown setting '{locked}'. Known: {string.Join(", ", PolicySettings.All)}.");

            if (document.IsPointer && PolicySourceReader.Validate(document.PolicyUrl!) is string bad)
                problems.Add($"policyUrl is not usable: {bad}");

            foreach (string problem in problems)
                Log.Warning("Organization policy {Path}: {Problem}", path, problem);

            Log.Information("Organization policy loaded from {Path} ({Locked} locked setting(s))",
                path, document.Locked.Count);
            return new PolicyState(document, path, PolicyOrigin.Loaded, problems);
        }
    }
}
