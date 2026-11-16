using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Versions;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Extensions;

/// <summary>
///     What the pages and AI clients do with extensions: create, edit, version, pin, turn off, remove. The commands in
///     <c>Features/</c> are thin wrappers over it.
/// </summary>
public sealed class ExtensionManager(ExtensionHost host, ExtensionCatalog catalog, ExtensionPaths paths, ExtensionStateStore store, IExtensionHistory history)
{
    private const long MaxReadBytes = 2 * 1024 * 1024;

    /// <summary>Gets the user saving versions: the Windows user.</summary>
    public static string Author => Environment.UserName;

    /// <summary>Gets the host the extensions are loaded by.</summary>
    public ExtensionHost Host => host;

    /// <summary>Gets the paths of the profile.</summary>
    public ExtensionPaths Paths => paths;

    /// <summary>Gets the history service.</summary>
    public IExtensionHistory History => history;

    /// <summary>Finds a loaded extension or throws <see cref="ExtensionErrorCodes.ExtensionNotFound" />.</summary>
    public ExtensionState Get(string id)
    {
        return host.GetState(id) ?? throw new CommandException(ExtensionErrorCodes.ExtensionNotFound, $"There is no extension '{id}'.");
    }

    /// <summary>Creates a new extension in the authoring root (or <paramref name="root" />), starts its history and loads it.</summary>
    public async Task<ExtensionState> CreateAsync(string name, string? id, string? description, ExtensionTemplate template, string? panel, string? root, CancellationToken cancellationToken)
    {
        var (directory, folderId, trimmed) = NewFolder(name, id, root);
        ExtensionScaffold.Create(directory, folderId, trimmed, description, template, panel, catalog.RevitVersion);
        if (history.IsAvailable)
        {
            history.Init(directory, Author, $"Created «{trimmed}»", ExtensionScaffold.SourceIgnore);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(folderId);
    }

    /// <summary>Creates an empty folder of buttons (an extension with no button yet), with its history started.</summary>
    public async Task<ExtensionState> CreateFolderAsync(string name, string? id, string? description, string? panel, string? root, CancellationToken cancellationToken, string? tab = null)
    {
        var (directory, folderId, trimmed) = NewFolder(name, id, root);
        ExtensionScaffold.CreateFolder(directory, folderId, trimmed, description, panel, tab);
        if (history.IsAvailable)
        {
            history.Init(directory, Author, $"Created the folder «{trimmed}»", ExtensionScaffold.SourceIgnore);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(folderId);
    }

    /// <summary>
    ///     Adds a button to an existing folder of the user: C# in <c>src\</c> and/or a page, and its entry in <c>plugin.json</c>;
    ///     saved as a version of the folder, then loaded.
    /// </summary>
    public async Task<ExtensionState> AddButtonAsync(string id, string name, string? description, ExtensionTemplate template, CancellationToken cancellationToken, string? tab = null, string? panel = null)
    {
        var state = Get(id);
        var descriptor = state.Descriptor;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        if (descriptor.Zone != ExtensionZone.Dev)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is an installed package: its buttons come with its updates. Add buttons to your own folders.");
        }

        if (descriptor.Kind == ExtensionKind.Dll && template != ExtensionTemplate.Page)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is built as a DLL by its author: add the command to its project. A page-only button can still be added here.");
        }

        var trimmed = name.Trim();
        var key = Usage.ButtonUsageStore.Normalize(trimmed);
        if (descriptor.Manifest.Ui?.EffectiveButtons().SelectMany(button => (button.Items ?? []).Prepend(button))
                .Any(button => string.Equals(Usage.ButtonUsageStore.Normalize(button.Name), key, StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The folder '{id}' has a button «{trimmed}» already.");
        }

        ExtensionScaffold.AddButton(descriptor.Directory, id, trimmed, description, template, catalog.RevitVersion, tab, panel);
        if (history.IsAvailable)
        {
            history.SaveVersion(descriptor.Directory, Author, $"Added the button «{trimmed}»", IgnoreFor(descriptor));
        }

        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false) ?? Get(id);
    }

    /// <summary>Renames a folder or changes its description (plugin.json <c>name</c> / <c>description</c>), saved as a version.</summary>
    public async Task<ExtensionState> UpdateFolderAsync(string id, string name, string? description, string? tab, string? panel, CancellationToken cancellationToken)
    {
        var descriptor = Own(id);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        ExtensionManifest.Update(descriptor.Directory, manifest =>
        {
            manifest["name"] = name.Trim();
            manifest["description"] = string.IsNullOrWhiteSpace(description) ? name.Trim() : description.Trim();
            if (tab is not null || panel is not null)
            {
                if (manifest["ui"] is not System.Text.Json.Nodes.JsonObject ui)
                {
                    manifest["ui"] = ui = new System.Text.Json.Nodes.JsonObject();
                }

                SetOrRemove(ui, "tab", tab);
                SetOrRemove(ui, "panel", panel);
            }
        });
        SaveChange(descriptor, $"Renamed the folder to «{name.Trim()}»");
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false) ?? Get(id);
    }

    /// <summary>
    ///     Renames a button or changes its tooltip in plugin.json, saved as a version. The command and its file keep their names:
    ///     the button is what the user sees, the code stays where it is.
    /// </summary>
    public async Task<ExtensionState> UpdateButtonAsync(string id, string button, string name, string? tooltip, string? tab, string? panel, CancellationToken cancellationToken)
    {
        var descriptor = Own(id);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        var key = Usage.ButtonUsageStore.Normalize(button);
        var newKey = Usage.ButtonUsageStore.Normalize(trimmed);
        var all = descriptor.Manifest.Ui?.EffectiveButtons().SelectMany(item => (item.Items ?? []).Prepend(item)).ToList() ?? [];
        if (!all.Any(item => Same(item.Name, key)))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"The folder '{id}' has no button «{button}».");
        }

        if (!Same(key, newKey) && all.Any(item => Same(item.Name, newKey)))
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The folder '{id}' has a button «{trimmed}» already.");
        }

        ExtensionManifest.Update(descriptor.Directory, manifest =>
        {
            foreach (var node in ButtonNodes(manifest["ui"] as System.Text.Json.Nodes.JsonObject))
            {
                if (node["name"]?.GetValue<string>() is { } current && Same(current, key))
                {
                    node["name"] = trimmed;
                    node["tooltip"] = string.IsNullOrWhiteSpace(tooltip) ? trimmed : tooltip.Trim();
                    SetOrRemove(node, "tab", tab);
                    SetOrRemove(node, "panel", panel);
                }
            }
        });
        SaveChange(descriptor, Same(key, newKey) ? $"Changed the description of «{trimmed}»" : $"Renamed «{key}» to «{trimmed}»");
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false) ?? Get(id);

        static bool Same(string left, string right) =>
            string.Equals(Usage.ButtonUsageStore.Normalize(left), Usage.ButtonUsageStore.Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Adds a panel (section) to a folder, or renames one: <c>ui.panels</c> keeps the order of the sections, and every button
    ///     of the old panel moves to the new name. Saved as a version.
    /// </summary>
    public async Task<ExtensionState> SetPanelAsync(string id, string? panel, string name, CancellationToken cancellationToken)
    {
        var descriptor = Own(id);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A panel needs a name.");
        }

        ExtensionManifest.Update(descriptor.Directory, manifest =>
        {
            var ui = Ui(manifest);
            var panels = PanelList(ui, descriptor);
            if (panel is null)
            {
                if (!panels.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    panels.Add(trimmed);
                }
            }
            else
            {
                var at = panels.FindIndex(item => string.Equals(item, panel, StringComparison.OrdinalIgnoreCase));
                if (at < 0)
                {
                    throw new CommandException(ExtensionErrorCodes.Invalid, $"The folder '{id}' has no panel «{panel}».");
                }

                panels[at] = trimmed;
                foreach (var node in ButtonNodes(ui))
                {
                    if (string.Equals(PanelOf(node, ui, descriptor), panel, StringComparison.OrdinalIgnoreCase))
                    {
                        node["panel"] = trimmed;
                    }
                }
            }

            ui["panels"] = new System.Text.Json.Nodes.JsonArray(panels.Select(item => (System.Text.Json.Nodes.JsonNode?)item).ToArray());
        });
        SaveChange(descriptor, panel is null ? $"Added the panel «{trimmed}»" : $"Renamed the panel «{panel}» to «{trimmed}»");
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false) ?? Get(id);
    }

    /// <summary>
    ///     Moves a button to another panel of its folder — before another button, or to the end — or to another folder of the
    ///     user. A move between folders carries its C# file and its page along, renames its command to the new folder
    ///     (<c>&lt;new id&gt;.&lt;Class&gt;</c>, also in the <c>AT.invoke</c> calls of its page) and is a version in both folders.
    /// </summary>
    public async Task<ExtensionState> MoveButtonAsync(string id, string button, string panel, string? before, string? toId, CancellationToken cancellationToken)
    {
        var source = Own(id);
        var target = string.IsNullOrWhiteSpace(toId) || string.Equals(toId, id, StringComparison.OrdinalIgnoreCase) ? source : Own(toId);
        var key = Usage.ButtonUsageStore.Normalize(button);
        var panelName = panel.Trim();
        if (panelName.Length == 0)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A panel is required.");
        }

        if (ReferenceEquals(target, source))
        {
            ExtensionManifest.Update(source.Directory, manifest =>
            {
                var ui = Ui(manifest);
                var node = TakeTopButton(ui, key) ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"The folder '{id}' has no button «{button}» on a panel.");
                node["panel"] = panelName;
                Insert(ui, node, before);
                AddPanel(ui, source, panelName);
            });
            SaveChange(source, $"Moved «{key}» to the panel «{panelName}»");
            return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false) ?? Get(id);
        }

        if (target.Kind == ExtensionKind.Dll)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{target.Id}' is built as a DLL: its commands live in its project.");
        }

        var entry = (source.Manifest.Ui?.EffectiveButtons() ?? []).FirstOrDefault(item => Same(item.Name, key))
                    ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"The folder '{id}' has no button «{button}» on a panel.");
        if ((target.Manifest.Ui?.EffectiveButtons() ?? []).SelectMany(item => (item.Items ?? []).Prepend(item)).Any(item => Same(item.Name, key)))
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"'{target.Id}' has a button «{key}» already.");
        }

        // The files of the button: its page, and the C# files of its command and of the commands its page calls
        string? page = null;
        if (string.IsNullOrWhiteSpace(entry.Command))
        {
            page = !string.IsNullOrWhiteSpace(entry.EntryHtml) ? entry.EntryHtml : source.Manifest.Ui?.EntryHtml ?? "index.html";
        }
        else if (!string.IsNullOrWhiteSpace(entry.EntryHtml))
        {
            page = entry.EntryHtml;
        }

        var classes = new List<string>();
        string? newCommand = null;
        if (entry.Command is { } command && command.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
        {
            classes.Add(command[(id.Length + 1)..]);
            newCommand = $"{target.Id}.{command[(id.Length + 1)..]}";
        }

        if (page is not null && File.Exists(Path.Combine(source.Directory, page)))
        {
            var calls = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(source.Directory, page)), $@"[""'`]{System.Text.RegularExpressions.Regex.Escape(id)}\.(\w+)[""'`]");
            classes.AddRange(calls.Select(match => match.Groups[1].Value));
        }

        var moves = new List<(string From, string To)>();
        var sources = ExtensionCatalog.GetSourceFiles(source.Directory);
        foreach (var className in classes.Distinct(StringComparer.Ordinal))
        {
            var file = sources.FirstOrDefault(path => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), $@"\bclass\s+{System.Text.RegularExpressions.Regex.Escape(className)}\b"));
            if (file is null || moves.Any(move => string.Equals(move.From, file, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source.Directory, file);
            if (File.Exists(Path.Combine(target.Directory, relative)))
            {
                throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"'{target.Id}' has a file {relative} already.");
            }

            moves.Add((file, Path.Combine(target.Directory, relative)));
        }

        string? newPage = null;
        if (page is not null && File.Exists(Path.Combine(source.Directory, page)))
        {
            newPage = File.Exists(Path.Combine(target.Directory, page)) ? Path.GetFileNameWithoutExtension(page) + "-" + ExtensionScaffold.ToId(key) + ".html" : page;
            moves.Add((Path.Combine(source.Directory, page), Path.Combine(target.Directory, newPage)));
        }

        // Save the unsaved work of both folders first: the move is then exactly one version in each
        SaveChange(source, $"Before moving «{key}» to {FolderTitle(target)}");
        SaveChange(target, $"Before taking «{key}» from {FolderTitle(source)}");
        foreach (var (from, to) in moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
            if (to.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                // The page calls the commands of its folder by their full names
                File.WriteAllText(to, File.ReadAllText(to).Replace($"'{id}.", $"'{target.Id}.").Replace($"\"{id}.", $"\"{target.Id}."));
            }
        }

        System.Text.Json.Nodes.JsonObject? moved = null;
        ExtensionManifest.Update(source.Directory, manifest =>
        {
            var ui = Ui(manifest);
            moved = TakeTopButton(ui, key);
            // A page that is gone is no longer the entry of the folder
            if (page is not null && string.Equals(ui["entryHtml"]?.GetValue<string>(), page, StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(source.Directory, page)))
            {
                ui.Remove("entryHtml");
            }
        });
        ExtensionManifest.Update(target.Directory, manifest =>
        {
            var ui = Ui(manifest);
            var node = moved!;
            node["panel"] = panelName;
            if (newCommand is not null)
            {
                node["command"] = newCommand;
            }

            if (newPage is not null)
            {
                node["entryHtml"] = newPage;
            }

            Insert(ui, node, before);
            AddPanel(ui, target, panelName);
            if (newCommand is not null && manifest["build"] is null && manifest["entryAssembly"] is null)
            {
                manifest["build"] = "host";
            }
        });
        SaveChange(source, $"Moved «{key}» to {FolderTitle(target)}");
        SaveChange(target, $"Took «{key}» from {FolderTitle(source)}");
        await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
        return await host.ReloadAsync(target.Id, cancellationToken).ConfigureAwait(false) ?? Get(target.Id);

        static bool Same(string left, string right) =>
            string.Equals(Usage.ButtonUsageStore.Normalize(left), Usage.ButtonUsageStore.Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string FolderTitle(ExtensionDescriptor descriptor)
    {
        return !string.IsNullOrWhiteSpace(descriptor.Manifest.Name) ? descriptor.Manifest.Name : descriptor.Id;
    }

    private static System.Text.Json.Nodes.JsonObject Ui(System.Text.Json.Nodes.JsonObject manifest)
    {
        if (manifest["ui"] is not System.Text.Json.Nodes.JsonObject ui)
        {
            manifest["ui"] = ui = new System.Text.Json.Nodes.JsonObject();
        }

        if (ui["buttons"] is not System.Text.Json.Nodes.JsonArray)
        {
            var buttons = new System.Text.Json.Nodes.JsonArray();
            if (ui["button"] is System.Text.Json.Nodes.JsonObject single)
            {
                ui.Remove("button");
                buttons.Add(single);
            }

            ui["buttons"] = buttons;
        }

        return ui;
    }

    /// <summary>The name of the folder: what its panel is when a button names none.</summary>
    private static string DefaultPanel(System.Text.Json.Nodes.JsonObject ui, ExtensionDescriptor descriptor)
    {
        return ui["panel"]?.GetValue<string>() is { Length: > 0 } panel ? panel : FolderTitle(descriptor);
    }

    private static string PanelOf(System.Text.Json.Nodes.JsonObject node, System.Text.Json.Nodes.JsonObject ui, ExtensionDescriptor descriptor)
    {
        return node["panel"]?.GetValue<string>() is { Length: > 0 } panel ? panel : DefaultPanel(ui, descriptor);
    }

    /// <summary>The sections in order: <c>ui.panels</c>, then the panels the buttons name.</summary>
    private static List<string> PanelList(System.Text.Json.Nodes.JsonObject ui, ExtensionDescriptor descriptor)
    {
        var panels = (ui["panels"] as System.Text.Json.Nodes.JsonArray)?.Select(item => item?.GetValue<string>()).OfType<string>().ToList() ?? [];
        foreach (var node in ((System.Text.Json.Nodes.JsonArray)ui["buttons"]!).OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var panel = PanelOf(node, ui, descriptor);
            if (!panels.Contains(panel, StringComparer.OrdinalIgnoreCase))
            {
                panels.Add(panel);
            }
        }

        return panels;
    }

    private static void AddPanel(System.Text.Json.Nodes.JsonObject ui, ExtensionDescriptor descriptor, string panel)
    {
        var panels = PanelList(ui, descriptor);
        if (!panels.Contains(panel, StringComparer.OrdinalIgnoreCase))
        {
            panels.Add(panel);
        }

        ui["panels"] = new System.Text.Json.Nodes.JsonArray(panels.Select(item => (System.Text.Json.Nodes.JsonNode?)item).ToArray());
    }

    /// <summary>Takes a button (or a pulldown) of the top level out of <c>ui.buttons</c>.</summary>
    private static System.Text.Json.Nodes.JsonObject? TakeTopButton(System.Text.Json.Nodes.JsonObject ui, string key)
    {
        var buttons = (System.Text.Json.Nodes.JsonArray)ui["buttons"]!;
        var node = buttons.OfType<System.Text.Json.Nodes.JsonObject>()
            .FirstOrDefault(item => string.Equals(Usage.ButtonUsageStore.Normalize(item["name"]?.GetValue<string>() ?? ""), key, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
        {
            buttons.Remove(node);
        }

        return node;
    }

    /// <summary>Puts a button before another one of <c>ui.buttons</c>, or at the end.</summary>
    private static void Insert(System.Text.Json.Nodes.JsonObject ui, System.Text.Json.Nodes.JsonObject node, string? before)
    {
        var buttons = (System.Text.Json.Nodes.JsonArray)ui["buttons"]!;
        var at = before is null
            ? -1
            : buttons.Select((item, index) => (item, index))
                .Where(pair => string.Equals(Usage.ButtonUsageStore.Normalize(pair.item?["name"]?.GetValue<string>() ?? ""), Usage.ButtonUsageStore.Normalize(before), StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.index)
                .DefaultIfEmpty(-1)
                .First();
        // An «order» of plugin.json would win over the position: the place the user chose is the place
        node.Remove("order");
        if (at < 0)
        {
            buttons.Add(node);
        }
        else
        {
            buttons.Insert(at, node);
        }
    }

    /// <summary>The button objects of <c>ui</c>: <c>button</c>, <c>buttons</c> and the <c>items</c> of pulldowns.</summary>
    private static IEnumerable<System.Text.Json.Nodes.JsonObject> ButtonNodes(System.Text.Json.Nodes.JsonObject? ui)
    {
        if (ui is null)
        {
            yield break;
        }

        var top = new List<System.Text.Json.Nodes.JsonObject>();
        if (ui["button"] is System.Text.Json.Nodes.JsonObject single)
        {
            top.Add(single);
        }

        if (ui["buttons"] is System.Text.Json.Nodes.JsonArray list)
        {
            top.AddRange(list.OfType<System.Text.Json.Nodes.JsonObject>());
        }

        foreach (var button in top)
        {
            yield return button;
            if (button["items"] is System.Text.Json.Nodes.JsonArray items)
            {
                foreach (var item in items.OfType<System.Text.Json.Nodes.JsonObject>())
                {
                    yield return item;
                }
            }
        }
    }

    /// <summary>Sets a text field of a JSON object: <see langword="null" /> keeps it, empty removes it (the default applies).</summary>
    private static void SetOrRemove(System.Text.Json.Nodes.JsonObject target, string field, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            target.Remove(field);
        }
        else
        {
            target[field] = value.Trim();
        }
    }

    /// <summary>An extension of the user's own folders: not a package, so its plugin.json is theirs to change.</summary>
    private ExtensionDescriptor Own(string id)
    {
        var descriptor = Get(id).Descriptor;
        if (descriptor.Zone != ExtensionZone.Dev)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is an installed package: its next update would undo the change.");
        }

        return descriptor;
    }

    private void SaveChange(ExtensionDescriptor descriptor, string message)
    {
        if (history.IsAvailable)
        {
            history.SaveVersion(descriptor.Directory, Author, message, IgnoreFor(descriptor));
        }
    }

    /// <summary>Checks a new folder's name and id and finds where it goes.</summary>
    private (string Directory, string Id, string Name) NewFolder(string name, string? id, string? root)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        id = string.IsNullOrWhiteSpace(id) ? ExtensionScaffold.ToId(name) : id.Trim();
        if (!ExtensionScaffold.IsValidId(id))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is not a valid id: use letters, digits, '.', '-' and '_'.");
        }

        if (host.GetState(id) is not null || catalog.Find(id) is not null)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"An extension with the id '{id}' exists already.");
        }

        var target = root is null ? paths.AuthoringRoot : paths.GetRoots().FirstOrDefault(candidate => ExtensionPaths.SamePath(candidate.Path, root) && candidate.Zone == ExtensionZone.Dev)?.Path
                                                         ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"'{root}' is not one of your folders of extensions.");
        var directory = Path.Combine(target, id);
        if (Directory.Exists(directory))
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The folder {directory} exists already.");
        }

        return (directory, id, name.Trim());
    }

    /// <summary>
    ///     Removes an extension: it leaves the pipeline, and its folder moves to <c>trash\</c> of the profile — with its history,
    ///     so nothing is lost by a wrong click.
    /// </summary>
    /// <returns>Where the folder went.</returns>
    public async Task<string> RemoveAsync(string id)
    {
        var state = Get(id);
        await host.UnloadAsync(id).ConfigureAwait(false);
        var trash = Path.Combine(paths.ProfileRoot, "trash", $"{ExtensionBuilder.AssemblyName(id)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        try
        {
            Directory.Move(state.Descriptor.Directory, trash);
        }
        catch (IOException)
        {
            // Another volume (a network share): copy, then delete
            CopyDirectory(state.Descriptor.Directory, trash);
            Directory.Delete(state.Descriptor.Directory, true);
        }

        store.SetPinnedVersion(id, null);
        return trash;
    }

    /// <summary>Turns an extension on or off and loads it again.</summary>
    public async Task<ExtensionState?> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        Get(id);
        store.SetEnabled(id, enabled);
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The files of an extension that belong to it: what git keeps, without builds.</summary>
    public IReadOnlyList<ExtensionFile> ListFiles(string id)
    {
        var directory = Get(id).Descriptor.Directory;
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => (Full: file, Relative: Path.GetRelativePath(directory, file).Replace('\\', '/')))
            .Where(file => !IsHidden(file.Relative))
            .Select(file => new ExtensionFile(file.Relative, new FileInfo(file.Full).Length, File.GetLastWriteTimeUtc(file.Full)))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads a text file of an extension.</summary>
    public string ReadFile(string id, string path)
    {
        var full = Resolve(id, path);
        if (!File.Exists(full))
        {
            throw new CommandException(ExtensionErrorCodes.FileNotFound, $"The extension '{id}' has no file {path}.");
        }

        if (new FileInfo(full).Length > MaxReadBytes)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"{path} is larger than {MaxReadBytes / 1024 / 1024} MB.");
        }

        return File.ReadAllText(full);
    }

    /// <summary>Writes a text file of an extension; the folder watcher then reloads it. Returns the compiler's verdict for a source.</summary>
    public async Task<ExtensionState?> WriteFileAsync(string id, string path, string content, CancellationToken cancellationToken)
    {
        var full = Resolve(id, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, cancellationToken).ConfigureAwait(false);
        // Not left to the watcher: the caller wants to know at once whether the code compiles
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a file of an extension.</summary>
    public async Task<ExtensionState?> DeleteFileAsync(string id, string path, CancellationToken cancellationToken)
    {
        var full = Resolve(id, path);
        if (!File.Exists(full))
        {
            throw new CommandException(ExtensionErrorCodes.FileNotFound, $"The extension '{id}' has no file {path}.");
        }

        File.Delete(full);
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The ignore list a history of this extension starts with.</summary>
    public static IReadOnlyList<string> IgnoreFor(ExtensionDescriptor descriptor)
    {
        return descriptor.Kind == ExtensionKind.Dll ? ExtensionScaffold.DllIgnore : ExtensionScaffold.SourceIgnore;
    }

    /// <summary>The version summary of an extension: whether it has a history, what changed, the latest version, the pin.</summary>
    public ExtensionVersionInfo GetVersionInfo(string id)
    {
        var state = Get(id);
        var directory = state.Descriptor.Directory;
        var initialized = history.IsInitialized(directory);
        var latest = initialized ? history.GetHistory(directory, limit: 1).FirstOrDefault() : null;
        var pinned = store.GetPinnedVersion(id);
        return new ExtensionVersionInfo(
            history.IsAvailable,
            history.UnavailableReason,
            initialized,
            initialized ? history.GetChanges(directory) : [],
            latest,
            pinned is null ? null : history.Find(directory, pinned) ?? new ExtensionVersion(pinned, pinned[..Math.Min(7, pinned.Length)], "(not found)", "", default),
            state.LoadedVersion);
    }

    /// <summary>Saves the current files as a version.</summary>
    public ExtensionVersion? SaveVersion(string id, string? message)
    {
        var state = Get(id);
        return history.SaveVersion(state.Descriptor.Directory, Author, message ?? "", IgnoreFor(state.Descriptor));
    }

    /// <summary>Brings back a version (of the whole extension or one file) and loads the result.</summary>
    public async Task<ExtensionVersion?> RestoreAsync(string id, string versionId, string? file, CancellationToken cancellationToken)
    {
        var state = Get(id);
        var version = history.Restore(state.Descriptor.Directory, Author, versionId, file);
        await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
        return version;
    }

    /// <summary>Pins the extension to a version — it runs that version's code whatever the files say — or unpins it.</summary>
    public async Task<ExtensionState?> PinAsync(string id, string? versionId, CancellationToken cancellationToken)
    {
        var state = Get(id);
        if (versionId is not null)
        {
            if (state.Descriptor.Kind != ExtensionKind.Source)
            {
                throw new CommandException(ExtensionErrorCodes.Invalid, "Only an extension built from sources can be pinned to a version; restore a version of a DLL extension instead.");
            }

            var version = history.Find(state.Descriptor.Directory, versionId)
                          ?? throw new CommandException(CommandErrorCodes.NotFound, $"There is no version {versionId} in '{id}'.");
            versionId = version.Id;
        }

        store.SetPinnedVersion(id, versionId);
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Installs a zip package into <c>extensions-dist\&lt;id&gt;</c>. An installed version is replaced atomically (extract
    ///     aside, swap, roll back on failure), and every install is saved as a version of the folder: the history of the
    ///     package's updates, and a way back to the previous one.
    /// </summary>
    public async Task<PackageInstallResult> InstallPackageAsync(string zipPath, bool overwrite, CancellationToken cancellationToken)
    {
        var info = Packages.ExtensionPackage.Validate(zipPath);
        var id = info.Manifest.Id;
        var existing = host.GetState(id);
        var twin = catalog.Scan().FirstOrDefault(descriptor => string.Equals(descriptor.Id, id, StringComparison.OrdinalIgnoreCase) && descriptor.Zone == ExtensionZone.Dev);
        if (twin is not null)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"'{id}' exists as your own extension in {twin.Directory}: remove or rename it first — one id exists once.");
        }

        var target = Path.Combine(paths.ManagedRoot, id);
        var exists = Directory.Exists(target);
        if (exists && !overwrite)
        {
            return new PackageInstallResult(id, info.Manifest.Version, target, Replaced: false, AlreadyInstalled: true, existing is null ? null : Features.ExtensionSummary.From(existing));
        }

        var staging = target + ".installing";
        var backup = target + ".old";
        DeleteDirectory(staging);
        DeleteDirectory(backup);
        await host.UnloadAsync(id).ConfigureAwait(false);
        try
        {
            Packages.ExtensionPackage.ExtractTo(zipPath, info, staging);
            // The history stays with the folder: an update is the next version of the same package
            if (exists && Directory.Exists(Path.Combine(target, ".git")))
            {
                CopyDirectory(Path.Combine(target, ".git"), Path.Combine(staging, ".git"));
            }

            if (exists)
            {
                Directory.Move(target, backup);
            }

            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (exists && !Directory.Exists(target))
                {
                    Directory.Move(backup, target);
                }

                throw;
            }

            DeleteDirectory(backup);
        }
        finally
        {
            DeleteDirectory(staging);
        }

        if (history.IsAvailable)
        {
            var descriptor = catalog.Read(target, ExtensionZone.Managed);
            history.SaveVersion(target, Author, $"Installed {info.Manifest.Version} from {Path.GetFileName(zipPath)}", descriptor is null ? ExtensionScaffold.DllIgnore : IgnoreFor(descriptor));
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        var state = host.GetState(id);
        return new PackageInstallResult(id, info.Manifest.Version, target, exists, false, state is null ? null : Features.ExtensionSummary.From(state));
    }

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, true);
    }

    /// <summary>A path inside the extension folder, never outside it nor into <c>.git</c>.</summary>
    private string Resolve(string id, string path)
    {
        var directory = Path.GetFullPath(Get(id).Descriptor.Directory);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A file path is required.");
        }

        var full = Path.GetFullPath(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(directory, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || IsHidden(relative.Replace('\\', '/')))
        {
            throw new CommandException(ExtensionErrorCodes.FileNotFound, $"{path} is not a file of the extension '{id}'.");
        }

        return full;
    }

    private static bool IsHidden(string relative)
    {
        var first = relative.Split('/')[0];
        return first is ".git" or "bin" or "obj" or ".vs" or ".editor" or "node_modules";
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
        }
    }
}

/// <summary>The result of installing a package.</summary>
/// <param name="Id">The id of the extension.</param>
/// <param name="Version">The version in the package.</param>
/// <param name="Directory">Where it is installed.</param>
/// <param name="Replaced">An installed version was replaced.</param>
/// <param name="AlreadyInstalled">It was installed already and <c>overwrite</c> was not asked for: nothing changed.</param>
/// <param name="Extension">The extension as loaded now.</param>
public sealed record PackageInstallResult(string Id, string Version, string Directory, bool Replaced, bool AlreadyInstalled, Features.ExtensionSummary? Extension);

/// <summary>A file of an extension.</summary>
/// <param name="Path">Relative to the extension folder, with <c>/</c>.</param>
/// <param name="Size">In bytes.</param>
/// <param name="Modified">When it last changed (UTC).</param>
public sealed record ExtensionFile(string Path, long Size, DateTime Modified);

/// <summary>The versions of an extension at a glance.</summary>
/// <param name="Available">Git can be used.</param>
/// <param name="UnavailableReason">Why it cannot.</param>
/// <param name="HasHistory">The folder has saved versions.</param>
/// <param name="Changes">The files changed since the latest version.</param>
/// <param name="Latest">The latest version.</param>
/// <param name="Pinned">The version the extension is pinned to.</param>
/// <param name="LoadedVersion">The version whose code is loaded: the pinned one, or <see langword="null" /> for the current files.</param>
public sealed record ExtensionVersionInfo(
    bool Available,
    string? UnavailableReason,
    bool HasHistory,
    IReadOnlyList<ExtensionChange> Changes,
    ExtensionVersion? Latest,
    ExtensionVersion? Pinned,
    string? LoadedVersion);
