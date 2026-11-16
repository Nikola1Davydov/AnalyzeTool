using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalyseTool.Extensions.Authoring;

namespace AnalyseTool.Extensions.Manifest;

/// <summary>A panel of a tab folder: a folder <c>&lt;Name&gt;.panel</c>.</summary>
/// <param name="Name">Its name (<c>bundle.json</c> <c>name</c>, else the folder name).</param>
/// <param name="Directory">The panel folder.</param>
/// <param name="Buttons">Its button folders in order — those of its pulldowns in place of the pulldown.</param>
public sealed record ExtensionPanel(string Name, string Directory, IReadOnlyList<string> Buttons);

/// <summary>
///     A tab of the user, laid out as pyRevit does it: <c>&lt;Tab&gt;.tab\&lt;Panel&gt;.panel\&lt;Button&gt;.pushbutton\</c>
///     (a <c>&lt;Name&gt;.pulldown\</c> in a panel holds push buttons too). The folders are the ribbon.
/// </summary>
/// <param name="Id">The id of the tab: the folder name as an id, with <c>.tab</c> (<c>Sheets.tab</c> → <c>sheets.tab</c>).</param>
/// <param name="Name">The name of the tab (<c>bundle.json</c> <c>name</c>, else the folder name).</param>
/// <param name="Directory">The tab folder.</param>
/// <param name="Panels">The panels in order.</param>
public sealed record ExtensionTab(string Id, string Name, string Directory, IReadOnlyList<ExtensionPanel> Panels)
{
    /// <summary>The start of the extension id of each of its buttons: the id without <c>.tab</c>.</summary>
    public string Prefix => Id[..^ButtonFolder.TabSuffix.Length];

    /// <summary>The button folders of the tab in order: panel by panel.</summary>
    public IEnumerable<string> ButtonDirectories => Panels.SelectMany(panel => panel.Buttons);
}

/// <summary>
///     The layout of a tab. A push button folder holds everything of one button — <c>bundle.json</c>, its C# (any <c>*.cs</c> in
///     it), its page, its <c>.git</c> — and is read as an extension of its own (id <c>&lt;tab&gt;.&lt;button&gt;</c>): its own
///     build, its own history. Every level may have a <c>bundle.json</c>: a name, and a <c>layout</c> that orders what is in it
///     (the rest follows by name). C# several buttons share lives in <c>_shared\</c> of the tab.
/// </summary>
public static class ButtonFolder
{
    /// <summary>The suffix of a tab folder.</summary>
    public const string TabSuffix = ".tab";

    /// <summary>The suffix of a panel folder.</summary>
    public const string PanelSuffix = ".panel";

    /// <summary>The suffix of a pulldown folder.</summary>
    public const string PulldownSuffix = ".pulldown";

    /// <summary>The suffix of a button folder.</summary>
    public const string ButtonSuffix = ".pushbutton";

    /// <summary>The file of every level: tab, panel, pulldown, button.</summary>
    public const string FileName = "bundle.json";

    /// <summary>The folder of a tab with the C# every button of it compiles with.</summary>
    public const string SharedFolder = "_shared";

    /// <summary>Whether a folder is a button folder.</summary>
    public static bool IsButton(string directory)
    {
        return Has(directory, ButtonSuffix);
    }

    /// <summary>Whether a folder is a tab folder.</summary>
    public static bool IsTab(string directory)
    {
        return Has(directory, TabSuffix);
    }

    /// <summary>The id of a folder of the layout: its name without the suffix, as an id.</summary>
    public static string IdOf(string directory)
    {
        return ExtensionScaffold.ToId(Bare(directory));
    }

    /// <summary>The name of a folder without its suffix (<c>Sheets.tab</c> → <c>Sheets</c>).</summary>
    public static string Bare(string directory)
    {
        var name = Path.GetFileName(directory.TrimEnd('\\', '/'));
        foreach (var suffix in new[] { TabSuffix, PanelSuffix, PulldownSuffix, ButtonSuffix })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    /// <summary>The tab folder a button folder is in; <see langword="null" /> outside a tab.</summary>
    public static string? TabDirectory(string buttonDirectory)
    {
        for (var directory = Path.GetDirectoryName(buttonDirectory.TrimEnd('\\', '/')); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (IsTab(directory))
            {
                return directory;
            }

            if (!Has(directory, PanelSuffix) && !Has(directory, PulldownSuffix))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The extension id of a button folder: <c>&lt;tab&gt;.&lt;button&gt;</c>.</summary>
    public static string ExtensionId(string buttonDirectory)
    {
        var tab = TabDirectory(buttonDirectory);
        return tab is null ? IdOf(buttonDirectory) : IdOf(tab) + "." + IdOf(buttonDirectory);
    }

    /// <summary>The id a tab folder of this name gets.</summary>
    public static string TabId(string name)
    {
        return ExtensionScaffold.ToId(name) + TabSuffix;
    }

    /// <summary>The display name of a folder of the layout: <c>bundle.json</c> <c>name</c> (or pyRevit's <c>title</c>), else the folder name.</summary>
    public static string NameOf(string directory)
    {
        var json = ReadObject(Path.Combine(directory, FileName));
        return Text(json?["name"]) ?? Text(json?["title"]) ?? Bare(directory);
    }

    /// <summary>Reads a tab folder: its panels and their buttons in the order of the <c>layout</c>s.</summary>
    public static ExtensionTab ReadTab(string directory)
    {
        var panels = Ordered(directory, PanelSuffix)
            .Select(panel => new ExtensionPanel(NameOf(panel), panel, Ordered(panel, ButtonSuffix, PulldownSuffix)
                .SelectMany(item => Has(item, PulldownSuffix) ? Ordered(item, ButtonSuffix) : [item])
                .ToList()))
            .ToList();
        return new ExtensionTab(IdOf(directory) + TabSuffix, NameOf(directory), directory, panels);
    }

    /// <summary>Changes the <c>bundle.json</c> of a folder in place; unknown fields stay.</summary>
    public static void UpdateBundle(string directory, Action<JsonObject> change)
    {
        var path = Path.Combine(directory, FileName);
        var json = ReadObject(path) ?? new JsonObject();
        change(json);
        WriteObject(path, json);
    }

    /// <summary>Writes the <c>layout</c> of a folder: the names of what is in it, in order.</summary>
    public static void SetLayout(string directory, IEnumerable<string> folders)
    {
        UpdateBundle(directory, json => json["layout"] = new JsonArray(folders.Select(folder => (JsonNode?)Path.GetFileName(folder.TrimEnd('\\', '/'))).ToArray()));
    }

    /// <summary>
    ///     The manifest of a button folder as plugin.json would have it — one button, built by the host — so the extension
    ///     machinery (build, load, history, statistics, MCP) treats a button like any extension.
    /// </summary>
    public static JsonObject ToPluginJson(string buttonDirectory)
    {
        var button = ReadObject(Path.Combine(buttonDirectory, FileName)) ?? new JsonObject();
        var id = ExtensionId(buttonDirectory);
        var entry = (JsonObject)button.DeepClone();
        var name = Text(entry["name"]) ?? Text(entry["title"]) ?? Bare(buttonDirectory);
        entry.Remove("title");
        entry["name"] = name;
        if (Text(entry["command"]) is { } className && !className.Contains('.'))
        {
            entry["command"] = $"{id}.{className}";
        }

        var page = Text(entry["page"]);
        entry.Remove("page");
        if (page is not null)
        {
            entry["entryHtml"] = page;
        }

        var parent = Path.GetDirectoryName(buttonDirectory.TrimEnd('\\', '/'))!;
        var pulldown = Has(parent, PulldownSuffix) ? parent : null;
        var panel = pulldown is null ? parent : Path.GetDirectoryName(pulldown)!;
        var tab = TabDirectory(buttonDirectory);
        JsonNode top = entry;
        if (pulldown is not null)
        {
            var group = ReadObject(Path.Combine(pulldown, FileName));
            top = new JsonObject { ["name"] = NameOf(pulldown), ["kind"] = "pulldown", ["items"] = new JsonArray(entry) };
            if (Text(group?["icon"]) is { } icon)
            {
                top["icon"] = icon;
            }
        }

        return new JsonObject
        {
            ["schema"] = 3,
            ["id"] = id,
            ["name"] = name,
            ["version"] = Text(button["version"]) ?? "1.0.0",
            ["description"] = Text(button["tooltip"]) ?? name,
            ["build"] = "host",
            ["ui"] = new JsonObject
            {
                ["tab"] = tab is null ? name : NameOf(tab),
                ["panel"] = Has(panel, PanelSuffix) ? NameOf(panel) : name,
                ["entryHtml"] = page ?? "index.html",
                ["buttons"] = new JsonArray(top)
            }
        };
    }

    /// <summary>Writes back what a change made to the plugin.json view of a button folder (<see cref="ToPluginJson" />).</summary>
    public static void FromPluginJson(string buttonDirectory, JsonObject view)
    {
        var path = Path.Combine(buttonDirectory, FileName);
        var button = ReadObject(path) ?? new JsonObject();
        var ui = view["ui"] as JsonObject;
        var top = (ui?["buttons"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() ?? ui?["button"] as JsonObject;
        var entry = top?["items"] is JsonArray items ? items.OfType<JsonObject>().FirstOrDefault() : top;
        if (entry is not null)
        {
            var id = ExtensionId(buttonDirectory);
            button.Remove("title");
            foreach (var (key, value) in entry.ToList())
            {
                var node = value?.DeepClone();
                switch (key)
                {
                    // Where the button sits is the folder it is in, not a field
                    case "tab" or "panel":
                        break;
                    case "command" when Text(node) is { } command && command.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase):
                        button["command"] = command[(id.Length + 1)..];
                        break;
                    case "entryHtml":
                        button["page"] = node;
                        break;
                    default:
                        button[key] = node;
                        break;
                }
            }

            foreach (var key in button.Select(pair => pair.Key).ToList())
            {
                var mapped = key == "page" ? "entryHtml" : key;
                if (!entry.ContainsKey(mapped) && key is not ("version" or "layout"))
                {
                    button.Remove(key);
                }
            }
        }

        WriteObject(path, button);
    }

    /// <summary>The C# a button compiles: its own <c>*.cs</c> (subfolders too) and the tab's <c>_shared\</c>.</summary>
    public static IReadOnlyList<string> SourceFiles(string buttonDirectory)
    {
        var own = Directory.Exists(buttonDirectory)
            ? Directory.GetFiles(buttonDirectory, "*.cs", SearchOption.AllDirectories).Where(file => !IsTooling(Path.GetRelativePath(buttonDirectory, file)))
            : [];
        var tab = TabDirectory(buttonDirectory);
        var shared = tab is null ? null : Path.Combine(tab, SharedFolder);
        var common = shared is not null && Directory.Exists(shared) ? Directory.GetFiles(shared, "*.cs", SearchOption.AllDirectories) : [];
        return own.Concat(common).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>What a panel or a pulldown holds, in order: its button and pulldown folders.</summary>
    public static IReadOnlyList<string> Children(string directory)
    {
        return Ordered(directory, ButtonSuffix, PulldownSuffix);
    }

    /// <summary>The panel folders of a tab, in order.</summary>
    public static IReadOnlyList<string> PanelDirectories(string tabDirectory)
    {
        return Ordered(tabDirectory, PanelSuffix);
    }

    /// <summary>The subfolders of a folder with one of the suffixes, ordered by its <c>layout</c>, then by name.</summary>
    private static List<string> Ordered(string directory, params string[] suffixes)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var children = Directory.GetDirectories(directory).Where(child => suffixes.Any(suffix => Has(child, suffix))).ToList();
        var layout = ReadObject(Path.Combine(directory, FileName))?["layout"] is JsonArray array
            ? array.Select(Text).OfType<string>().ToList()
            : [];
        return children
            .OrderBy(child =>
            {
                var at = layout.FindIndex(item => string.Equals(item, Path.GetFileName(child), StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(item, Bare(child), StringComparison.OrdinalIgnoreCase));
                return at < 0 ? int.MaxValue : at;
            })
            .ThenBy(child => Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Has(string directory, string suffix)
    {
        return Path.GetFileName(directory.TrimEnd('\\', '/')).EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTooling(string relative)
    {
        var first = relative.Split('\\', '/')[0];
        return first is "bin" or "obj" or ".git" or ".editor" or ".vs";
    }

    private static string? Text(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim().Length > 0 ? text.Trim() : null;
    }

    private static JsonObject? ReadObject(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid {Path.GetFileName(path)} in {Path.GetDirectoryName(path)}: {exception.Message}", exception);
        }
    }

    private static void WriteObject(string path, JsonObject json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
