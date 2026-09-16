using AnalyseTool.Core.Common.Extensions;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>
    /// The two policy settings that turn the Extension Manager from "installs what the user pastes"
    /// into "installs what the organization approved": a prefix whitelist for sources and feeds, and
    /// the switch for the free-form repository paste. Both read the current <see cref="PolicyStore"/>;
    /// the matching itself is a pure function so it can be tested without a file.
    /// </summary>
    internal static class PolicyFeedRules
    {
        /// <summary>Null when the source may be used; otherwise the sentence to show the user.</summary>
        public static string? Refusal(string source)
        {
            PolicyState policy = PolicyStore.Current;
            List<string>? allowed = policy.Document.Extensions?.AllowedFeeds;
            if (!policy.IsPresent || allowed is null) return null;

            string normalized = ExtensionUpdateFeed.Normalize(source);
            if (IsAllowed(normalized, allowed)) return null;

            string who = policy.Document.Organization?.Name ?? "your organization";
            return $"'{normalized}' is not an approved extension source. {who} allows: " +
                   $"{string.Join(", ", allowed)} ({policy.Path}).";
        }

        /// <summary>The free-form "Install from repository…" paste is allowed unless the policy says otherwise.</summary>
        public static bool InstallFromRepositoryAllowed
        {
            get
            {
                PolicyState policy = PolicyStore.Current;
                return !policy.IsPresent || policy.Document.Extensions?.AllowInstallFromRepository != false;
            }
        }

        public static string InstallFromRepositoryRefusal()
        {
            PolicyState policy = PolicyStore.Current;
            string who = policy.Document.Organization?.Name ?? "Your organization";
            return $"{who} allows installing extensions from its catalog only ({policy.Path}).";
        }

        /// <summary>Prefix match against the whitelist. Entries are compared in the same normalized form
        /// the sources use (<c>github:owner/repo</c>, <c>https://host/path</c>). A bare
        /// <c>github:owner</c> is read as <c>github:owner/</c> so it cannot match another owner whose
        /// name merely starts the same way.</summary>
        public static bool IsAllowed(string normalizedSource, IEnumerable<string> allowedFeeds)
        {
            foreach (string raw in allowedFeeds)
            {
                string entry = ExtensionUpdateFeed.Normalize(raw);
                if (entry.Length == 0) continue;

                if (entry.StartsWith("github:", StringComparison.OrdinalIgnoreCase) && !entry.Contains('/'))
                    entry += "/";

                if (normalizedSource.StartsWith(entry, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
