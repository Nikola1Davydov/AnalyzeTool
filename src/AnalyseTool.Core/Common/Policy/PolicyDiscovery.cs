using System.IO;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>One place a policy might be, and how it was guessed.</summary>
    internal sealed record PolicyCandidate(string Reference, string How);

    /// <summary>
    /// Turns what a user typed (or nothing) into policy locations to try (design §8). No network here:
    /// the candidates are strings; <see cref="OrgPolicySource.FetchAsync"/> tries them.
    /// </summary>
    internal static class PolicyDiscovery
    {
        public const string WellKnownPath = "/.well-known/analysetool/policy.json";

        public static IReadOnlyList<PolicyCandidate> Candidates(string? input)
        {
            List<PolicyCandidate> result = new();
            string value = (input ?? string.Empty).Trim();

            if (value.Length > 0)
            {
                if (PolicySourceResolver.IsReference(value))
                    result.Add(new PolicyCandidate(value, "named source"));
                else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    result.Add(new PolicyCandidate(value, "URL"));
                else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    result.Add(new PolicyCandidate("https://" + value["http://".Length..], "URL (upgraded to https)"));
                else if (LooksLikePath(value))
                    result.Add(new PolicyCandidate(
                        value.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? value : Path.Combine(value, "policy.json"), "folder"));
                else
                    result.Add(new PolicyCandidate($"https://{value.TrimEnd('/')}{WellKnownPath}", "well-known URL of the domain"));
                return result;
            }

            string? domain = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
            if (!string.IsNullOrWhiteSpace(domain))
                result.Add(new PolicyCandidate($"https://{domain.Trim().ToLowerInvariant()}{WellKnownPath}", "well-known URL of this computer's domain"));

            // A synced library that carries a marker pointing at its policy.
            foreach (string root in OneDriveLibraries.MountPoints())
            {
                string? found = FindPolicyMarker(root, depth: 3);
                if (found is not null) result.Add(new PolicyCandidate(found, "marker file in a synced folder"));
            }
            return result;
        }

        internal static bool LooksLikePath(string value) =>
            value.StartsWith(@"\\") || value.Length > 2 && value[1] == ':' || value.StartsWith('%');

        private static string? FindPolicyMarker(string root, int depth)
        {
            try
            {
                string marker = Path.Combine(root, PolicySourceResolver.MarkerFileName);
                if (File.Exists(marker))
                {
                    string? policy = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(marker))["policy"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(policy))
                    {
                        string full = Path.IsPathRooted(policy) ? policy : Path.Combine(root, policy);
                        if (File.Exists(full)) return full;
                    }
                    else if (File.Exists(Path.Combine(root, "policy.json")))
                        return Path.Combine(root, "policy.json");
                }
                if (depth <= 0) return null;
                foreach (string child in Directory.EnumerateDirectories(root))
                {
                    string? found = FindPolicyMarker(child, depth - 1);
                    if (found is not null) return found;
                }
            }
            catch { /* unreadable */ }
            return null;
        }
    }
}
