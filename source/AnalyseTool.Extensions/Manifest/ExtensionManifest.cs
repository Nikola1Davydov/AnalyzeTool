using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AnalyseTool.Extensions.Manifest;

/// <summary>
///     The <c>plugin.json</c> of an extension folder — the format of AnalyseTool 1.x, read unchanged. An extension may have
///     C# commands (sources in <c>src\</c> or a DLL named by <see cref="EntryAssembly" />), a page (<see cref="Ui" />), or
///     both.
/// </summary>
public sealed record ExtensionManifest
{
    /// <summary>The file name of the manifest.</summary>
    public const string FileName = "plugin.json";

    /// <summary>Gets the version of the manifest format; absent means 1.</summary>
    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    /// <summary>Gets the stable unique id; also the prefix of the extension's command names (<c>&lt;id&gt;.&lt;Command&gt;</c>).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the version the author gave the extension.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Gets a short description for listings.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Gets the author.</summary>
    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    /// <summary>Gets the author's website.</summary>
    [JsonPropertyName("website")]
    public string? Website { get; init; }

    /// <summary>Gets where users get help.</summary>
    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; init; }

    /// <summary>Gets the icon of the extension in listings, relative to the folder.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>Gets the update feed: an HTTPS URL or <c>github:owner/repo</c>.</summary>
    [JsonPropertyName("updateFeed")]
    public string? UpdateFeed { get; init; }

    /// <summary>
    ///     Gets the assembly with the commands, relative to the folder, resolved <c>&lt;year&gt;\</c> first. For an extension
    ///     the host builds from <c>src\</c> it names the assembly the host writes.
    /// </summary>
    [JsonPropertyName("entryAssembly")]
    public string? EntryAssembly { get; init; }

    /// <summary>
    ///     Gets who builds the commands: <c>host</c> — AnalyseTool compiles <c>src\*.cs</c> (what a new extension gets),
    ///     <c>author</c> — the author ships a DLL. Absent: <c>host</c> when there are sources and no project file, as in 1.x.
    /// </summary>
    [JsonPropertyName("build")]
    public string? Build { get; init; }

    /// <summary>Gets the page and the ribbon buttons.</summary>
    [JsonPropertyName("ui")]
    public ExtensionUi? Ui { get; init; }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Reads the manifest of a folder.</summary>
    /// <exception cref="InvalidDataException">The file is not a manifest: invalid JSON or no <c>id</c>.</exception>
    public static ExtensionManifest Read(string directory)
    {
        var path = Path.Combine(directory, FileName);
        ExtensionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(path), ReadOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid {FileName}: {exception.Message}", exception);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidDataException($"{FileName} has no 'id'.");
        }

        return manifest;
    }

    /// <summary>
    ///     Changes fields of the manifest in place: every field the change does not touch — including ones this version does not
    ///     know — stays as the author wrote it.
    /// </summary>
    public static void Update(string directory, Action<JsonObject> change)
    {
        var path = Path.Combine(directory, FileName);
        var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new InvalidDataException($"{FileName} is not a JSON object.");
        change(node);
        File.WriteAllText(path, node.ToJsonString(WriteOptions));
    }

    /// <summary>Writes a new manifest.</summary>
    public static void Write(string directory, JsonObject manifest)
    {
        File.WriteAllText(Path.Combine(directory, FileName), manifest.ToJsonString(WriteOptions));
    }
}

/// <summary>The page and the ribbon buttons of an extension.</summary>
public sealed record ExtensionUi
{
    /// <summary>Gets the page, relative to the folder.</summary>
    [JsonPropertyName("entryHtml")]
    public string EntryHtml { get; init; } = "index.html";

    /// <summary>Gets a dev server that serves the page instead of the files (Vite with HMR).</summary>
    [JsonPropertyName("devUrl")]
    public string? DevUrl { get; init; }

    /// <summary>Gets whether the page opens in the dock pane instead of a window.</summary>
    [JsonPropertyName("dockable")]
    public bool Dockable { get; init; }

    /// <summary>Gets the ribbon tab; the AnalyseTool tab when absent.</summary>
    [JsonPropertyName("tab")]
    public string? Tab { get; init; }

    /// <summary>Gets the ribbon panel.</summary>
    [JsonPropertyName("panel")]
    public string? Panel { get; init; }

    /// <summary>Gets the single button (schema 1).</summary>
    [JsonPropertyName("button")]
    public ExtensionButton? Button { get; init; }

    /// <summary>Gets several buttons (schema 2).</summary>
    [JsonPropertyName("buttons")]
    public IReadOnlyList<ExtensionButton>? Buttons { get; init; }

    /// <summary>The buttons to build, from either form, by <see cref="ExtensionButton.Order" /> then declaration; 0 goes last.</summary>
    public IReadOnlyList<ExtensionButton> EffectiveButtons()
    {
        if (Buttons is { Count: > 0 })
        {
            return Buttons.Select((button, index) => (button, index))
                .OrderBy(entry => entry.button.Order == 0 ? int.MaxValue : entry.button.Order)
                .ThenBy(entry => entry.index)
                .Select(entry => entry.button)
                .ToList();
        }

        return Button is null ? [] : [Button];
    }
}

/// <summary>A ribbon button of an extension: it runs a command or opens the page.</summary>
public sealed record ExtensionButton
{
    /// <summary>Gets the text of the button; also the title of its window.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the tooltip.</summary>
    [JsonPropertyName("tooltip")]
    public string? Tooltip { get; init; }

    /// <summary>Gets the icon: a PNG relative to the folder, or <c>glyph:E8A9</c> (Segoe MDL2 Assets).</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>Gets the command a click runs; the button opens the page when absent.</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Gets the arguments the click passes to <see cref="Command" />, e.g. <c>{ "route": "/settings" }</c> for <c>OpenWindow</c>.</summary>
    [JsonPropertyName("args")]
    public JsonElement? Args { get; init; }

    /// <summary>Gets the page of this button, overriding <see cref="ExtensionUi.EntryHtml" />.</summary>
    [JsonPropertyName("entryHtml")]
    public string? EntryHtml { get; init; }

    /// <summary>Gets whether this button's page opens in the dock pane.</summary>
    [JsonPropertyName("dockable")]
    public bool? Dockable { get; init; }

    /// <summary>Gets the ribbon tab of this button.</summary>
    [JsonPropertyName("tab")]
    public string? Tab { get; init; }

    /// <summary>Gets the ribbon panel of this button.</summary>
    [JsonPropertyName("panel")]
    public string? Panel { get; init; }

    /// <summary>Gets the order on the panel: lower first, 0 last.</summary>
    [JsonPropertyName("order")]
    public int Order { get; init; }

    /// <summary>Gets the shape: <c>push</c>, <c>stacked</c> or <c>pulldown</c>.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    /// <summary>Gets the entries of a pulldown.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<ExtensionButton>? Items { get; init; }

    /// <summary>Gets the normalized <see cref="Kind" />; an unknown value is a push button.</summary>
    [JsonIgnore]
    public ButtonKind ResolvedKind => Kind?.Trim().ToLowerInvariant() switch
    {
        "stacked" => ButtonKind.Stacked,
        "pulldown" => ButtonKind.Pulldown,
        _ => ButtonKind.Push
    };
}

/// <summary>The ribbon shape of a button.</summary>
public enum ButtonKind
{
    /// <summary>A large button.</summary>
    Push,

    /// <summary>A small button in a column of three.</summary>
    Stacked,

    /// <summary>A button with a list under it.</summary>
    Pulldown
}
