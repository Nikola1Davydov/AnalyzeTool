using Newtonsoft.Json;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>
    /// Shape of the per-extension <c>plugin.json</c> sitting next to the extension files.
    /// An extension may ship C# commands (<see cref="EntryAssembly"/>), a JS UI (<see cref="Ui"/>),
    /// or both — every field except identity is optional.
    /// </summary>
    internal sealed record ExtensionManifest
    {
        /// <summary>Manifest FORMAT version — not the extension's version. Absent means 1, so every
        /// manifest written before this field stays valid. The host reads older schemas for the life of
        /// a major: a migration is an offer, never a requirement. Without this field neither the host
        /// nor an agent can tell an old plugin.json from a new one, which is the whole point (#127).</summary>
        [JsonProperty("schema")]
        public int Schema { get; init; } = 1;

        /// <summary>Stable unique id, also used as the command namespace prefix (e.g. "acme.tools").</summary>
        [JsonProperty("id")]
        public string Id { get; init; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; init; } = string.Empty;

        // ---- Manifest v2: optional vendor metadata, shown by the Extension Manager. Additive —
        // ---- v1 manifests without these fields keep working unchanged.

        /// <summary>Short human-readable description for listings.</summary>
        [JsonProperty("description")]
        public string? Description { get; init; }

        /// <summary>Vendor / author display name.</summary>
        [JsonProperty("publisher")]
        public string? Publisher { get; init; }

        /// <summary>Vendor homepage URL.</summary>
        [JsonProperty("website")]
        public string? Website { get; init; }

        /// <summary>Where users get help — errors are attributed to "extension X by Y, contact …".</summary>
        [JsonProperty("supportUrl")]
        public string? SupportUrl { get; init; }

        /// <summary>Extension-level icon (PNG relative to the extension folder) for manager listings.
        /// Distinct from <c>ui.button.icon</c> (the ribbon button); the manager falls back to that.</summary>
        [JsonProperty("icon")]
        public string? Icon { get; init; }

        /// <summary>Optional update feed: either an HTTPS URL returning <c>{version, downloadUrl}</c>
        /// or the shortcut <c>github:owner/repo</c> (latest release, zip asset). Updates download
        /// from the VENDOR's URL — AnalyseTool never hosts third-party binaries.</summary>
        [JsonProperty("updateFeed")]
        public string? UpdateFeed { get; init; }

        /// <summary>Optional C# assembly with IRevitTask commands (relative to the extension folder;
        /// resolved year-subfolder-first, e.g. <c>2025\Ext.dll</c>, then the folder root).
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

        /// <summary>When true, the ribbon button shows this extension inside AnalyseTool's shared dockable
        /// pane (swapping its content) instead of opening a separate window. Lets extensions live in the
        /// dock with no per-extension pane registration (which Revit only allows at startup).</summary>
        [JsonProperty("dockable")]
        public bool Dockable { get; init; }

        /// <summary>Optional ribbon tab name. When omitted the host's default tab is used.</summary>
        [JsonProperty("tab")]
        public string? Tab { get; init; }

        /// <summary>Optional ribbon panel name within the tab.</summary>
        [JsonProperty("panel")]
        public string? Panel { get; init; }

        /// <summary>Ribbon button definition — the SINGLE-button form (schema 1). Still supported and
        /// still the right choice for an extension with one surface.</summary>
        [JsonProperty("button")]
        public ExtensionButton? Button { get; init; }

        /// <summary>Several ribbon buttons, each able to open its own page in its own way. An extension
        /// with two surfaces — a manager window and a dockable palette, say — cannot be expressed by
        /// <see cref="Button"/> alone, because entry page and dockability live per BUTTON, not per
        /// extension. Additive: when this is absent the singular form is used unchanged.</summary>
        [JsonProperty("buttons")]
        public IReadOnlyList<ExtensionButton>? Buttons { get; init; }

        /// <summary>The buttons to build, from whichever form the manifest used. Ordered by
        /// <see cref="ExtensionButton.Order"/>, then by declaration — so an author who does not care
        /// about order gets the order they wrote.</summary>
        public IReadOnlyList<ExtensionButton> EffectiveButtons()
        {
            if (Buttons is { Count: > 0 })
                return Buttons.Select((b, i) => (b, i)).OrderBy(t => t.b.Order).ThenBy(t => t.i)
                              .Select(t => t.b).ToList();
            return Button is null ? Array.Empty<ExtensionButton>() : new[] { Button };
        }
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

        /// <summary>When set, clicking the ribbon button INVOKES this command (e.g. "&lt;id&gt;.Foo")
        /// directly instead of opening a WebView window. Used by command-only script extensions
        /// (SaveAsCommand). When null, the button opens the extension's UI page.</summary>
        [JsonProperty("command")]
        public string? Command { get; init; }

        // ---- Per-button overrides (schema 2). Null means "inherit from ui.*", so a single-button
        // ---- manifest never has to repeat what it already said one level up.

        /// <summary>Entry HTML for THIS button, overriding <see cref="ExtensionUi.EntryHtml"/>. Two
        /// buttons of one extension normally open two different pages.</summary>
        [JsonProperty("entryHtml")]
        public string? EntryHtml { get; init; }

        /// <summary>Whether THIS button shows in the shared dockable pane, overriding
        /// <see cref="ExtensionUi.Dockable"/>. Dockability is a property of a surface, not of an
        /// extension: a manager opens as a window while its placement palette belongs in the dock.</summary>
        [JsonProperty("dockable")]
        public bool? Dockable { get; init; }

        /// <summary>Ribbon tab for THIS button, overriding <see cref="ExtensionUi.Tab"/>.</summary>
        [JsonProperty("tab")]
        public string? Tab { get; init; }

        /// <summary>Ribbon panel for THIS button, overriding <see cref="ExtensionUi.Panel"/>.</summary>
        [JsonProperty("panel")]
        public string? Panel { get; init; }

        /// <summary>Sort order within the panel; equal values keep declaration order.</summary>
        [JsonProperty("order")]
        public int Order { get; init; }
    }
}
