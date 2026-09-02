using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>What a vendor's update feed reports for the latest release.</summary>
    internal sealed record ExtensionUpdateInfo(string Version, string DownloadUrl, string? ReleaseUrl);

    /// <summary>
    /// Resolves a manifest's <c>updateFeed</c> (see <see cref="ExtensionManifest.UpdateFeed"/>).
    /// Two forms:
    /// <list type="bullet">
    /// <item><c>github:owner/repo</c> — GitHub releases/latest; version = tag (leading 'v' stripped),
    /// download = the release's first .zip asset. Zero server infrastructure for the vendor.</item>
    /// <item>an HTTPS URL returning <c>{"version": "...", "downloadUrl": "..."}</c> — for vendors
    /// with their own hosting.</item>
    /// </list>
    /// AnalyseTool is only the courier: downloads always come from the VENDOR's URL (#48).
    /// </summary>
    internal static class ExtensionUpdateFeed
    {
        private static readonly HttpClient Http = new();
        private static readonly Regex GithubRef = new(@"^github:(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // A repository link is what a person has in their hand — from the catalog, a README or a
        // colleague. Accepting only "github:owner/repo" would make the common paste fail with a
        // parser message, so the pasted forms are folded onto the same branch instead. The API
        // endpoint is included because it is the one URL people reach for when they read "an https
        // feed" and think of GitHub — and following it as a plain JSON feed fails, since the release
        // JSON carries tag_name/assets, not {version, downloadUrl}.
        private static readonly Regex GithubUrl = new(
            @"^https://(?:www\.)?github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\.git)?/?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GithubApiUrl = new(
            @"^https://api\.github\.com/repos/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/releases/latest/?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ShorthandRef = new(
            @"^(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)$",
            RegexOptions.Compiled);

        /// <summary>Folds the forms a person can paste — a repository URL, the releases API URL,
        /// bare <c>owner/repo</c> — onto the canonical <c>github:owner/repo</c>. Anything else is
        /// returned trimmed and judged by <see cref="ResolveAsync"/>.</summary>
        public static string Normalize(string source)
        {
            source = (source ?? string.Empty).Trim();

            foreach (Regex pattern in new[] { GithubUrl, GithubApiUrl, ShorthandRef })
            {
                Match m = pattern.Match(source);
                if (m.Success)
                    return $"github:{m.Groups["owner"].Value}/{m.Groups["repo"].Value}";
            }

            return source;
        }

        /// <param name="extensionId">The id the feed was declared for — used to pick the right asset
        /// when a release carries several zips.</param>
        public static async Task<ExtensionUpdateInfo> ResolveAsync(string feed, string extensionId, CancellationToken ct)
        {
            feed = Normalize(feed);

            Match github = GithubRef.Match(feed);
            if (github.Success)
                return await ResolveGithubAsync(github.Groups["owner"].Value, github.Groups["repo"].Value, extensionId, ct);

            if (feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return await ResolveJsonFeedAsync(feed, ct);

            throw new InvalidOperationException(
                $"Unsupported updateFeed '{feed}' — expected 'github:owner/repo' or an https:// URL.");
        }

        private static async Task<ExtensionUpdateInfo> ResolveGithubAsync(
            string owner, string repo, string extensionId, CancellationToken ct)
        {
            string json = await GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", ct);
            GitHubRelease? release = JsonConvert.DeserializeObject<GitHubRelease>(json);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                throw new InvalidOperationException($"github:{owner}/{repo} has no published release.");

            string version = release.TagName!.TrimStart('v', 'V');

            // Re-running a release workflow does not replace a release, it EDITS it — so assets
            // accumulate, and a repo that published 1.0.0 and 1.0.1 under one tag carries both zips.
            // Picking "the first zip" there is a coin flip that installs the wrong version silently,
            // so an ambiguous release is an error naming what it found. PackExtension emits
            // <id>-<version>.zip, which is what makes the unambiguous pick possible.
            List<GitHubAsset> zips = (release.Assets ?? new())
                .Where(a => !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl)
                            && a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (zips.Count == 0)
                throw new InvalidOperationException(
                    $"The latest release of github:{owner}/{repo} ({release.TagName}) has no .zip asset.");

            GitHubAsset? asset = zips.Count == 1
                ? zips[0]
                : zips.FirstOrDefault(a => string.Equals(a.Name, $"{extensionId}-{version}.zip",
                                                         StringComparison.OrdinalIgnoreCase));
            if (asset is null)
                throw new InvalidOperationException(
                    $"Release {release.TagName} of github:{owner}/{repo} carries {zips.Count} zip assets and none " +
                    $"is named '{extensionId}-{version}.zip', so the right package cannot be identified. " +
                    $"Found: {string.Join(", ", zips.Select(a => a.Name))}. Publish one package per release.");

            return new ExtensionUpdateInfo(version, asset.BrowserDownloadUrl!, release.HtmlUrl);
        }

        private static async Task<ExtensionUpdateInfo> ResolveJsonFeedAsync(string url, CancellationToken ct)
        {
            string json = await GetStringAsync(url, ct);
            JsonFeed? feed = JsonConvert.DeserializeObject<JsonFeed>(json);
            if (feed is null || string.IsNullOrWhiteSpace(feed.Version) || string.IsNullOrWhiteSpace(feed.DownloadUrl))
                throw new InvalidOperationException(
                    "The update feed did not return the expected {\"version\", \"downloadUrl\"} JSON.");
            return new ExtensionUpdateInfo(feed.Version, feed.DownloadUrl, feed.ReleaseUrl);
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            // Per-request UA (GitHub requires one); never mutate the shared client's defaults.
            request.Headers.UserAgent.Add(
                new ProductInfoHeaderValue(SharedData.ToolData.PLUGIN_NAME, SharedData.ToolData.PLUGIN_VERSION));
            using HttpResponseMessage response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }

        /// <summary>Downloads the vendor's package to a local temp file and returns its path.</summary>
        public static async Task<string> DownloadPackageAsync(string url, string extensionId, CancellationToken ct)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing non-https download URL: {url}");

            string dir = System.IO.Path.Combine(PathProvider.ProfilePath, "cache", "downloads");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, extensionId + ".zip");

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(
                new ProductInfoHeaderValue(SharedData.ToolData.PLUGIN_NAME, SharedData.ToolData.PLUGIN_VERSION));
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using (System.IO.FileStream target = System.IO.File.Create(file))
                await response.Content.CopyToAsync(target, ct);

            return file;
        }

        private sealed record GitHubRelease
        {
            [JsonProperty("tag_name")] public string? TagName { get; init; }
            [JsonProperty("html_url")] public string? HtmlUrl { get; init; }
            [JsonProperty("assets")] public List<GitHubAsset>? Assets { get; init; }
        }

        private sealed record GitHubAsset
        {
            [JsonProperty("name")] public string? Name { get; init; }
            [JsonProperty("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
        }

        private sealed record JsonFeed
        {
            [JsonProperty("version")] public string? Version { get; init; }
            [JsonProperty("downloadUrl")] public string? DownloadUrl { get; init; }
            [JsonProperty("releaseUrl")] public string? ReleaseUrl { get; init; }
        }
    }
}
