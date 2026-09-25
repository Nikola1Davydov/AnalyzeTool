using AnalyseTool.Core.Common.Utils;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Serilog;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AnalyseTool.App.Features
{
    [RevitCommand(
        Description = "Checks the release feed for a newer AnalyseTool version and returns update info. " +
                      "Network call; does not touch the Revit model.",
        ReadOnly = true)]
    internal sealed class CheckUpdate : IRevitTask
    {
        // Headers are set ONCE here: the client is shared by every call, and mutating DefaultRequestHeaders
        // per request races when two windows check at the same time. GitHub requires a User-Agent.
        private static readonly HttpClient _httpClient = CreateClient();

        // The main window and the Settings window both ask on open; the anonymous GitHub API allows 60
        // requests an hour. A release does not appear more often than every few minutes, so a short TTL
        // costs nothing and keeps a busy session well inside the limit. Failures are not cached.
        private static readonly AsyncTtlCache<UpdateInfo> _cache = new(TimeSpan.FromMinutes(15));

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            return await _cache.GetOrCreateAsync(
                token => CheckForUpdateAsync(owner: "Nikola1Davydov", repo: "AnalyzeTool", token), ct);
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(SharedData.ToolData.PLUGIN_NAME, SharedData.ToolData.PLUGIN_VERSION));
            return client;
        }

        private static async Task<UpdateInfo?> CheckForUpdateAsync(string owner, string repo, CancellationToken ct)
        {
            string currentVersionString = SharedData.ToolData.PLUGIN_VERSION;
            Version currentVersion = new Version(currentVersionString);

            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(ct);

                GitHubReleaseDto? dto = JsonConvert.DeserializeObject<GitHubReleaseDto>(json);

                if (dto == null || string.IsNullOrWhiteSpace(dto.tag_name))
                    return null;

                // Example: tag_name = "v1.2.3" or "1.2.3"
                string cleanedTag = dto.tag_name.TrimStart('v', 'V');

                if (!Version.TryParse(cleanedTag, out Version? latestVersion))
                    return null;

                bool hasUpdate = latestVersion > currentVersion;

                return new UpdateInfo
                {
                    IsUpdateAvailable = hasUpdate,
                    CurrentVersion = currentVersionString,
                    LatestVersion = latestVersion.ToString(),
                    ReleaseUrl = dto.html_url
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Offline, rate-limited or a feed hiccup: "no update info" is the answer, not an error.
                // A cancellation by the caller propagates — that one is not a failed check.
                Log.Warning("Update check failed: {Message}", ex.Message);
                return null;
            }
        }
        private record GitHubReleaseDto
        {
            public string tag_name { get; set; } = string.Empty;
            public string html_url { get; set; } = string.Empty;
        }
    }
}
