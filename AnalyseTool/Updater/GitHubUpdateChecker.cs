using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace AnalyseTool.Updater
{
    internal class GitHubUpdateChecker
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        public static async Task<UpdateInfo?> CheckForUpdateAsync(string owner,string repo)
        {
            string currentVersionString = SharedData.ToolData.PLUGIN_VERSION;
            Version currentVersion = new Version(currentVersionString);

            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            // GitHub requires User-Agent header
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(SharedData.ToolData.PLUGIN_NAME, currentVersionString)); // name can be anything

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();

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
            catch
            {
                // You can log here if needed
                return null;
            }
        }
    }
}
