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

        /// <summary>The effective policy. Loaded on first access; call <see cref="Reload"/> after the
        /// file changed.</summary>
        public static PolicyState Current
        {
            get
            {
                lock (Gate)
                    return _current ??= Load(_pathOverride ?? PathProvider.PolicyPath);
            }
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

        /// <summary>Reads and validates one policy file. Pure: no cache, no global state — the unit
        /// the tier-1 tests exercise.</summary>
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

            if (document.IsPointer)
                problems.Add("This file points at a policy URL (policyUrl). Following pointers is not " +
                             "supported by this plugin version yet; only the settings in this file apply.");

            foreach (string problem in problems)
                Log.Warning("Organization policy {Path}: {Problem}", path, problem);

            Log.Information("Organization policy loaded from {Path} ({Locked} locked setting(s))",
                path, document.Locked.Count);
            return new PolicyState(document, path, PolicyOrigin.Loaded, problems);
        }
    }
}
