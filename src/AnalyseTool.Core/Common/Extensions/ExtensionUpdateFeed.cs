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

        public static async Task<ExtensionUpdateInfo> ResolveAsync(string feed, CancellationToken ct)
        {
            feed = feed.Trim();

            Match github = GithubRef.Match(feed);
            if (github.Success)
                return await ResolveGithubAsync(github.Groups["owner"].Value, github.Groups["repo"].Value, ct);

            if (feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return await ResolveJsonFeedAsync(feed, ct);

            throw new InvalidOperationException(
                $"Unsupported updateFeed '{feed}' — expected 'github:owner/repo' or an https:// URL.");
        }

        private static async Task<ExtensionUpdateInfo> ResolveGithubAsync(string owner, string repo, CancellationToken ct)
        {
            string json = await GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", ct);
            GitHubRelease? release = JsonConvert.DeserializeObject<GitHubRelease>(json);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                throw new InvalidOperationException($"github:{owner}/{repo} has no published release.");

            string version = release.TagName.TrimStart('v', 'V');
            string? zipUrl = release.Assets?
                .FirstOrDefault(a => a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                ?.BrowserDownloadUrl;
            if (string.IsNullOrWhiteSpace(zipUrl))
                throw new InvalidOperationException(
                    $"The latest release of github:{owner}/{repo} ({release.TagName}) has no .zip asset.");

            return new ExtensionUpdateInfo(version, zipUrl!, release.HtmlUrl);
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
