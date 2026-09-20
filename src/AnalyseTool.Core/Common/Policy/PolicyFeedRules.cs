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

        /// <summary>The whitelist governs the FEED origin; the package URL is whatever the feed serves
        /// (GitHub assets come from <c>*.githubusercontent.com</c>, a JSON feed can name any host). When
        /// a whitelist exists, the download must come from the feed's own host, from GitHub's asset
        /// hosts for a <c>github:</c> feed, or itself match the whitelist. Null = allowed.</summary>
        public static string? DownloadRefusal(string feedSource, string downloadUrl)
        {
            PolicyState policy = PolicyStore.Current;
            List<string>? allowed = policy.Document.Extensions?.AllowedFeeds;
            if (!policy.IsPresent || allowed is null) return null;

            return IsDownloadAllowed(ExtensionUpdateFeed.Normalize(feedSource), downloadUrl, allowed)
                ? null
                : $"The feed '{feedSource}' serves its package from '{downloadUrl}', which is neither the feed's " +
                  $"own host nor an approved source ({policy.Path}). Refusing to download it.";
        }

        /// <summary>Pure form of <see cref="DownloadRefusal"/>.</summary>
        public static bool IsDownloadAllowed(string normalizedFeed, string downloadUrl, IEnumerable<string> allowedFeeds)
        {
            // A file feed (share, synced library, source: reference) serves files: the package must be a
            // file too — a feed on disk that redirects the download to the internet is refused.
            bool fileFeed = ExtensionUpdateFeed.IsFilePath(normalizedFeed) || PolicySourceResolver.IsReference(normalizedFeed);
            bool fileDownload = ExtensionUpdateFeed.IsFilePath(downloadUrl) || PolicySourceResolver.IsReference(downloadUrl);
            if (fileFeed) return fileDownload;
            if (fileDownload) return false;

            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? download)) return false;
            if (!string.Equals(download.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;

            if (normalizedFeed.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                return download.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                    || download.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

            if (Uri.TryCreate(normalizedFeed, UriKind.Absolute, out Uri? feed)
                && download.Host.Equals(feed.Host, StringComparison.OrdinalIgnoreCase))
                return true;

            return IsAllowed(downloadUrl, allowedFeeds);
        }

        /// <summary>Prefix match against the whitelist. Entries are compared in the same normalized form
        /// the sources use (<c>github:owner/repo</c>, <c>https://host/path</c>). A bare
        /// <c>github:owner</c> is read as <c>github:owner/</c> so it cannot match another owner whose
        /// name merely starts the same way.</summary>
        public static bool IsAllowed(string normalizedSource, IEnumerable<string> allowedFeeds)
        {
            // A named source is approved by definition: the policy that declares the whitelist declares
            // the source. A raw path must be listed as a prefix like any URL.
            if (PolicySourceResolver.IsReference(normalizedSource)) return true;

            foreach (string raw in allowedFeeds)
            {
                string entry = ExtensionUpdateFeed.Normalize(raw);
                if (entry.Length == 0) continue;

                if (entry.StartsWith("github:", StringComparison.OrdinalIgnoreCase) && !entry.Contains('/'))
                    entry += "/";
                // "https://git.company.local" must not match "https://git.company.local.evil.example/…":
                // a bare host is a host, so the prefix ends at its slash.
                if (entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && Uri.TryCreate(entry, UriKind.Absolute, out Uri? u) && u.AbsolutePath == "/" && !entry.EndsWith('/'))
                    entry += "/";

                if (normalizedSource.StartsWith(entry, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
