using Newtonsoft.Json;

namespace AnalyseTool.Infrastructure.Extensions
{
    /// <summary>
    /// Shape of the per-extension <c>plugin.json</c> sitting next to the extension assembly.
    /// The same file is reused later by JS extensions (UI fields added then).
    /// </summary>
    internal sealed record ExtensionManifest
    {
        /// <summary>Stable unique id, also used as the command namespace prefix (e.g. "acme.tools").</summary>
        [JsonProperty("id")]
        public string Id { get; init; } = string.Empty;

        [JsonProperty("displayName")]
        public string DisplayName { get; init; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; init; } = string.Empty;

        /// <summary>Target Revit major, "R25" or "R26". Must match the running host.</summary>
        [JsonProperty("targetRevit")]
        public string TargetRevit { get; init; } = string.Empty;

        /// <summary>AnalyseTool.Sdk version the extension was built against (major must match host).</summary>
        [JsonProperty("sdkVersion")]
        public string SdkVersion { get; init; } = string.Empty;

        /// <summary>File name of the C# assembly to load (relative to the extension folder).</summary>
        [JsonProperty("entryAssembly")]
        public string EntryAssembly { get; init; } = string.Empty;
    }
}
