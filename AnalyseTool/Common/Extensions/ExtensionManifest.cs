using Newtonsoft.Json;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>
    /// Shape of the per-extension <c>plugin.json</c> sitting next to the extension files.
    /// An extension may ship C# commands (<see cref="EntryAssembly"/>), a JS UI (<see cref="Ui"/>),
    /// or both — every field except identity is optional.
    /// </summary>
    internal sealed record ExtensionManifest
    {
        /// <summary>Stable unique id, also used as the command namespace prefix (e.g. "acme.tools").</summary>
        [JsonProperty("id")]
        public string Id { get; init; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; init; } = string.Empty;

        /// <summary>Optional C# assembly with IRevitTask commands (relative to the extension folder).
        /// SDK compatibility is derived automatically from the DLL's AnalyseTool.Sdk reference.</summary>
        [JsonProperty("entryAssembly")]
        public string? EntryAssembly { get; init; }

        /// <summary>Optional JS UI surfaced as a ribbon button opening a WebView page.</summary>
        [JsonProperty("ui")]
        public ExtensionUi? Ui { get; init; }
    }

    /// <summary>JS UI description for an extension.</summary>
    internal sealed record ExtensionUi
    {
        /// <summary>Entry HTML file (relative to the extension folder), e.g. "index.html".</summary>
        [JsonProperty("entryHtml")]
        public string EntryHtml { get; init; } = "index.html";

        /// <summary>Optional dev-server URL (e.g. "http://localhost:5173"). When set, the window loads
        /// it instead of the built files — enables Vite/HMR live development. Remove for release.</summary>
        [JsonProperty("devUrl")]
        public string? DevUrl { get; init; }

        /// <summary>Optional ribbon tab name. When omitted the host's default tab is used.</summary>
        [JsonProperty("tab")]
        public string? Tab { get; init; }

        /// <summary>Optional ribbon panel name within the tab.</summary>
        [JsonProperty("panel")]
        public string? Panel { get; init; }

        /// <summary>Ribbon button definition.</summary>
        [JsonProperty("button")]
        public ExtensionButton? Button { get; init; }
    }

    /// <summary>Ribbon button metadata for a JS extension. <see cref="Name"/> also serves as the
    /// extension's display label (ribbon button + window title).</summary>
    internal sealed record ExtensionButton
    {
        [JsonProperty("name")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("tooltip")]
        public string? Tooltip { get; init; }

        /// <summary>Icon file relative to the extension folder (PNG).</summary>
        [JsonProperty("icon")]
        public string? Icon { get; init; }
    }
}
