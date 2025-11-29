using Newtonsoft.Json;

namespace AnalyseTool.Updater
{
    public class UpdateInfo
    {
        [JsonProperty("isUpdateAvailable")]
        public bool IsUpdateAvailable { get; set; }
        [JsonProperty("latestVersion")]
        public string LatestVersion { get; set; } = string.Empty;
        [JsonProperty("currentVersion")]
        public string CurrentVersion { get; set; } = SharedData.ToolData.PLUGIN_VERSION;
        [JsonProperty("releaseUrl")]
        public string? ReleaseUrl { get; set; }
    }
}
