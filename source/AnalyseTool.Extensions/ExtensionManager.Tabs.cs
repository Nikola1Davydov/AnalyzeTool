using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Extensions;

// The tabs of the user, laid out as pyRevit does: <Tab>.tab\<Panel>.panel\<Button>.pushbutton\. A tab and a panel are folders
// with a bundle.json (name, layout); a button folder is an extension of its own with its own history. Moving a button is
// moving its folder.
public sealed partial class ExtensionManager
{
    /// <summary>Whether an id names a tab (<c>sheets.tab</c>) rather than an extension.</summary>
    public static bool IsTab(string id)
    {
        return id.EndsWith(ButtonFolder.TabSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds a tab of the user or throws <see cref="ExtensionErrorCodes.ExtensionNotFound" />.</summary>
    public ExtensionTab GetTab(string id)
    {
        return catalog.FindTab(id) ?? throw new CommandException(ExtensionErrorCodes.ExtensionNotFound, $"There is no tab '{id}'.");
    }

    /// <summary>Whether the user has a tab on (all its buttons load).</summary>
    public bool IsTabEnabled(string id)
    {
        return store.IsEnabled(id);
    }

    /// <summary>The loaded buttons of a tab, in its order.</summary>
    public IReadOnlyList<Loading.ExtensionState> TabButtons(ExtensionTab tab)
    {
        return tab.ButtonDirectories
            .Select(directory => host.GetStates().FirstOrDefault(state => ExtensionPaths.SamePath(state.Descriptor.Directory, directory)))
            .OfType<Loading.ExtensionState>()
            .ToList();
    }

    /// <summary>Creates an empty tab folder <c>&lt;Name&gt;.tab\</c> in the authoring root (or <paramref name="root" />).</summary>
    public async Task<ExtensionTab> CreateTabAsync(string name, string? root, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        var id = ButtonFolder.TabId(name);
        if (catalog.FindTab(id) is not null)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"There is a tab «{name.Trim()}» already.");
        }

        try
        {
            ExtensionScaffold.CreateTab(ResolveRoot(root), name);
        }
        catch (IOException exception)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, exception.Message);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return GetTab(id);
    }

    /// <summary>
    ///     Adds a button to a tab: a folder <c>&lt;Name&gt;.pushbutton\</c> in its panel (made when new) with its C# and/or page,
    ///     its own history started, then loaded.
    /// </summary>
    public async Task<Loading.ExtensionState> AddTabButtonAsync(string tabId, string name, string? description, ExtensionTemplate template, string? panel, CancellationToken cancellationToken)
    {
        var tab = GetTab(tabId);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        var id = tab.Prefix + "." + ExtensionScaffold.ToId(ExtensionScaffold.FolderName(name));
        if (tab.ButtonDirectories.Any(directory => string.Equals(ButtonFolder.ExtensionId(directory), id, StringComparison.OrdinalIgnoreCase)) || host.GetState(id) is not null)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The tab «{tab.Name}» has a button «{name.Trim()}» already.");
        }

        var panelName = string.IsNullOrWhiteSpace(panel) ? tab.Panels.FirstOrDefault()?.Name ?? tab.Name : panel.Trim();
        string directory;
        try
        {
            directory = ExtensionScaffold.AddPushButton(tab.Directory, panelName, name, description, template, catalog.RevitVersion);
        }
        catch (IOException exception)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, exception.Message);
        }

        if (history.IsAvailable)
        {
            history.Init(directory, Author, $"Created «{name.Trim()}»", ExtensionScaffold.SourceIgnore);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(ButtonFolder.ExtensionId(directory));
    }

    /// <summary>Renames a tab (<c>bundle.json</c> <c>name</c>; the folder and the ids stay).</summary>
    public async Task<ExtensionTab> RenameTabAsync(string tabId, string name, CancellationToken cancellationToken)
    {
        var tab = GetTab(tabId);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        ButtonFolder.UpdateBundle(tab.Directory, json =>
        {
            json.Remove("title");
            json["name"] = name.Trim();
        });
        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return GetTab(tabId);
    }

    /// <summary>
    ///     Adds a panel to a tab (a new <c>&lt;Name&gt;.panel\</c>, last in the layout) or renames one: its folder is renamed when
    ///     nothing holds it, else its <c>bundle.json</c> gets the name.
    /// </summary>
    public async Task<ExtensionTab> SetTabPanelAsync(string tabId, string? panel, string name, CancellationToken cancellationToken)
    {
        var tab = GetTab(tabId);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A panel needs a name.");
        }

        var existing = tab.Panels.FirstOrDefault(item => string.Equals(item.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (panel is null)
        {
            if (existing is null)
            {
                var directory = Path.Combine(tab.Directory, ExtensionScaffold.FolderName(trimmed) + ButtonFolder.PanelSuffix);
                Directory.CreateDirectory(directory);
                ButtonFolder.UpdateBundle(directory, json => json["name"] = trimmed);
                ButtonFolder.SetLayout(tab.Directory, tab.Panels.Select(item => item.Directory).Append(directory));
            }
        }
        else
        {
            var current = tab.Panels.FirstOrDefault(item => string.Equals(item.Name, panel, StringComparison.OrdinalIgnoreCase))
                          ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"The tab «{tab.Name}» has no panel «{panel}».");
            if (existing is not null && !ReferenceEquals(existing, current))
            {
                throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The tab «{tab.Name}» has a panel «{trimmed}» already.");
            }

            var renamed = Path.Combine(tab.Directory, ExtensionScaffold.FolderName(trimmed) + ButtonFolder.PanelSuffix);
            var order = tab.Panels.Select(item => item.Directory).ToList();
            if (!ExtensionPaths.SamePath(renamed, current.Directory) && !Directory.Exists(renamed))
            {
                // The buttons in it leave the pipeline while their folders move
                foreach (var state in TabButtons(tab).Where(state => state.Descriptor.Directory.StartsWith(current.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    await host.UnloadAsync(state.Descriptor.Id).ConfigureAwait(false);
                }

                try
                {
                    Directory.Move(current.Directory, renamed);
                    order[order.IndexOf(current.Directory)] = renamed;
                    current = current with { Directory = renamed };
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A file of it is open: the folder keeps its name, bundle.json says the new one
                }
            }

            ButtonFolder.UpdateBundle(current.Directory, json =>
            {
                json.Remove("title");
                json["name"] = trimmed;
            });
            ButtonFolder.SetLayout(tab.Directory, order);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return GetTab(tabId);
    }

    /// <summary>
    ///     Moves a button of a tab: within its panel, to another panel, or to another tab — its folder moves with its history.
    ///     In another tab its id changes (<c>&lt;tab&gt;.&lt;button&gt;</c>), so the <c>AT.invoke</c> calls of its pages follow, as
    ///     a version of the button.
    /// </summary>
    public async Task<Loading.ExtensionState> MoveTabButtonAsync(string tabId, string button, string panel, string? before, string? toTabId, CancellationToken cancellationToken)
    {
        var tab = GetTab(tabId);
        var target = string.IsNullOrWhiteSpace(toTabId) || string.Equals(toTabId, tabId, StringComparison.OrdinalIgnoreCase) ? tab : GetTab(toTabId);
        var key = Usage.ButtonUsageStore.Normalize(button);
        var source = tab.ButtonDirectories.FirstOrDefault(directory => Same(ButtonFolder.NameOf(directory), key))
                     ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"The tab «{tab.Name}» has no button «{button}».");
        var oldId = ButtonFolder.ExtensionId(source);
        var panelName = panel.Trim();
        if (panelName.Length == 0)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A panel is required.");
        }

        var panelDirectory = EnsurePanel(target, panelName);

        var oldParent = Path.GetDirectoryName(source)!;
        var destination = source;
        if (!ExtensionPaths.SamePath(oldParent, panelDirectory))
        {
            destination = Path.Combine(panelDirectory, Path.GetFileName(source));
            if (Directory.Exists(destination))
            {
                throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The panel «{panelName}» has a folder {Path.GetFileName(source)} already.");
            }

            var newId = target.Prefix + "." + ButtonFolder.IdOf(source);
            if (!string.Equals(newId, oldId, StringComparison.OrdinalIgnoreCase) && host.GetState(newId) is not null)
            {
                throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The tab «{target.Name}» has a button «{key}» already.");
            }

            if (history.IsAvailable)
            {
                history.SaveVersion(source, Author, $"Before moving «{key}» to «{target.Name} › {panelName}»", ExtensionScaffold.SourceIgnore);
            }

            await host.UnloadAsync(oldId).ConfigureAwait(false);
            try
            {
                Directory.Move(source, destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
                throw new CommandException(ExtensionErrorCodes.Invalid, $"The folder of «{key}» is in use and cannot move: {exception.Message}");
            }

            if (!string.Equals(newId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                // Its page calls its command by the full name
                foreach (var page in Directory.GetFiles(destination, "*.html", SearchOption.AllDirectories))
                {
                    var text = File.ReadAllText(page);
                    var changed = text.Replace($"'{oldId}.", $"'{newId}.").Replace($"\"{oldId}.", $"\"{newId}.").Replace($"`{oldId}.", $"`{newId}.");
                    if (changed != text)
                    {
                        File.WriteAllText(page, changed);
                    }
                }

                store.SetPinnedVersion(oldId, null);
            }

            if (history.IsAvailable)
            {
                history.SaveVersion(destination, Author, $"Moved «{key}» to «{target.Name} › {panelName}»", ExtensionScaffold.SourceIgnore);
            }

            if (Directory.Exists(oldParent))
            {
                ButtonFolder.SetLayout(oldParent, ButtonFolder.Children(oldParent));
            }
        }

        // Its place among the others
        var children = ButtonFolder.Children(panelDirectory).Where(child => !ExtensionPaths.SamePath(child, destination)).ToList();
        var at = before is null ? -1 : children.FindIndex(child => Same(ButtonFolder.NameOf(child), before));
        children.Insert(at < 0 ? children.Count : at, destination);
        ButtonFolder.SetLayout(panelDirectory, children);

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(ButtonFolder.ExtensionId(destination));

        static bool Same(string left, string right) =>
            string.Equals(Usage.ButtonUsageStore.Normalize(left), Usage.ButtonUsageStore.Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Turns a whole tab on or off: its buttons stay as they are, they just do not load while it is off.</summary>
    public async Task<ExtensionTab> SetTabEnabledAsync(string tabId, bool enabled, CancellationToken cancellationToken)
    {
        var tab = GetTab(tabId);
        store.SetEnabled(tab.Id, enabled);
        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return tab;
    }

    /// <summary>Removes a tab: its buttons leave the pipeline and the folder moves to <c>trash\</c> with every history in it.</summary>
    /// <returns>Where the folder went.</returns>
    public async Task<string> RemoveTabAsync(string tabId)
    {
        var tab = GetTab(tabId);
        foreach (var state in TabButtons(tab))
        {
            await host.UnloadAsync(state.Descriptor.Id).ConfigureAwait(false);
        }

        var trash = Path.Combine(paths.ProfileRoot, "trash", $"{Path.GetFileName(tab.Directory)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        try
        {
            Directory.Move(tab.Directory, trash);
        }
        catch (IOException)
        {
            CopyDirectory(tab.Directory, trash);
            Directory.Delete(tab.Directory, true);
        }

        return trash;
    }

    /// <summary>
    ///     Moves a button of a folder of plugin.json (the 1.x layout) into a tab: it becomes a folder <c>&lt;Name&gt;.pushbutton\</c>
    ///     with its C# (the files of its command and of the commands its page calls — moved, or copied while another button of
    ///     the old folder still uses them; C# without a command, helpers, is copied), its page as <c>index.html</c> and its icon,
    ///     with a new history. The old folder saves a version; a folder left with no button and no C# goes to the trash.
    /// </summary>
    public async Task<Loading.ExtensionState> MoveToTabAsync(string id, string button, string tabId, string panel, string? before, CancellationToken cancellationToken)
    {
        var source = Own(id);
        var tab = GetTab(tabId);
        var key = Usage.ButtonUsageStore.Normalize(button);
        var all = (source.Manifest.Ui?.EffectiveButtons() ?? []).SelectMany(item => item.ResolvedKind == ButtonKind.Pulldown ? item.Items ?? [] : [item]).ToList();
        var entry = all.FirstOrDefault(item => Same(item.Name, key)) ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"The folder '{id}' has no button «{button}».");
        if (source.Kind == ExtensionKind.Dll && !string.IsNullOrWhiteSpace(entry.Command) && entry.Command.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is built as a DLL: the command of «{key}» lives in its project, not in C# a tab could compile.");
        }

        var panelName = string.IsNullOrWhiteSpace(panel) ? tab.Panels.FirstOrDefault()?.Name ?? tab.Name : panel.Trim();
        var panelDirectory = EnsurePanel(tab, panelName);
        var directory = Path.Combine(panelDirectory, ExtensionScaffold.FolderName(key) + ButtonFolder.ButtonSuffix);
        var newId = tab.Prefix + "." + ButtonFolder.IdOf(directory);
        if (Directory.Exists(directory) || host.GetState(newId) is not null)
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"The tab «{tab.Name}» has a button «{key}» already.");
        }

        var (classes, page) = PartsOf(source, entry);
        var others = all.Where(item => !ReferenceEquals(item, entry)).Select(item => PartsOf(source, item)).ToList();
        var stillUsed = others.SelectMany(item => item.Classes).ToHashSet(StringComparer.Ordinal);
        var pagesUsed = others.Select(item => item.Page).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        SaveChange(source, $"Before moving «{key}» to the tab «{tab.Name}»");
        Directory.CreateDirectory(directory);
        var remove = new List<string>();
        var hasCode = false;
        foreach (var file in Catalog.ExtensionCatalog.GetSourceFiles(source.Directory))
        {
            var text = File.ReadAllText(file);
            var declared = System.Text.RegularExpressions.Regex.Matches(text, @"\bclass\s+(\w+)").Select(match => match.Groups[1].Value).ToList();
            var mine = declared.Any(classes.Contains);
            var helper = !text.Contains("IRevitTask", StringComparison.Ordinal);
            if (!mine && !helper)
            {
                continue;
            }

            File.Copy(file, FreeName(directory, Path.GetFileName(file)));
            hasCode = true;
            if (mine && !declared.Any(stillUsed.Contains))
            {
                remove.Add(file);
            }
        }

        string? newPage = null;
        if (page is not null && File.Exists(Path.Combine(source.Directory, page)))
        {
            newPage = "index.html";
            var text = File.ReadAllText(Path.Combine(source.Directory, page));
            File.WriteAllText(Path.Combine(directory, newPage), text.Replace($"'{id}.", $"'{newId}.").Replace($"\"{id}.", $"\"{newId}.").Replace($"`{id}.", $"`{newId}."));
            if (!pagesUsed.Contains(page))
            {
                remove.Add(Path.Combine(source.Directory, page));
            }
        }

        string? icon = null;
        if (entry.Icon is { Length: > 0 } path && !path.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase)
                                               && Path.GetFullPath(Path.Combine(source.Directory, path)) is var full && full.StartsWith(source.Directory, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
        {
            icon = Path.GetFileName(full);
            File.Copy(full, Path.Combine(directory, icon));
        }

        // Its entry of plugin.json, as bundle.json: where it sits is now the folder
        System.Text.Json.Nodes.JsonObject? node = null;
        var folderDockable = source.Manifest.Ui?.Dockable == true;
        ExtensionManifest.Update(source.Directory, manifest =>
        {
            foreach (var candidate in ButtonNodes(manifest["ui"] as System.Text.Json.Nodes.JsonObject).ToList())
            {
                if (candidate["items"] is null && candidate["name"]?.GetValue<string>() is { } name && Same(name, key))
                {
                    node = candidate;
                    if (candidate.Parent is System.Text.Json.Nodes.JsonArray array)
                    {
                        array.Remove(candidate);
                    }
                    else
                    {
                        // The single ui.button of schema 1
                        ((System.Text.Json.Nodes.JsonObject)manifest["ui"]!).Remove("button");
                    }

                    break;
                }
            }
        });
        var bundle = (System.Text.Json.Nodes.JsonObject?)node?.DeepClone() ?? new System.Text.Json.Nodes.JsonObject { ["name"] = key };
        foreach (var field in new[] { "tab", "panel", "order", "entryHtml" })
        {
            bundle.Remove(field);
        }

        if (bundle["command"]?.GetValue<string>() is { } command && command.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
        {
            bundle["command"] = command[(id.Length + 1)..];
        }

        if (newPage is not null)
        {
            bundle["page"] = newPage;
        }

        if (icon is not null)
        {
            bundle["icon"] = icon;
        }

        if (folderDockable && bundle["dockable"] is null && page is not null)
        {
            bundle["dockable"] = true;
        }

        ButtonFolder.UpdateBundle(directory, json =>
        {
            foreach (var (field, value) in bundle)
            {
                json[field] = value?.DeepClone();
            }
        });
        File.WriteAllText(Path.Combine(directory, "AGENTS.md"), ExtensionScaffold.AgentsPointer(key));
        File.WriteAllText(Path.Combine(directory, "CLAUDE.md"), ExtensionScaffold.ClaudePointer);
        File.WriteAllText(Path.Combine(directory, ".gitignore"), string.Join("\n", ExtensionScaffold.SourceIgnore) + "\n");
        if (hasCode)
        {
            ExtensionScaffold.WriteEditorProject(directory, newId, catalog.RevitVersion);
        }

        var children = ButtonFolder.Children(panelDirectory).Where(child => !ExtensionPaths.SamePath(child, directory)).ToList();
        var at = before is null ? -1 : children.FindIndex(child => Same(ButtonFolder.NameOf(child), before));
        children.Insert(at < 0 ? children.Count : at, directory);
        ButtonFolder.SetLayout(panelDirectory, children);

        foreach (var file in remove)
        {
            File.Delete(file);
        }

        if (history.IsAvailable)
        {
            history.Init(directory, Author, $"Moved «{key}» from «{FolderTitle(source)}»", ExtensionScaffold.SourceIgnore);
        }

        SaveChange(source, $"Moved «{key}» to the tab «{tab.Name}»");
        var left = ExtensionManifest.Read(source.Directory);
        if ((left.Ui?.EffectiveButtons().Count ?? 0) == 0 && Catalog.ExtensionCatalog.GetSourceFiles(source.Directory).Count == 0)
        {
            // Nothing left of the old folder: it goes, with its history, to the trash
            await RemoveAsync(id).ConfigureAwait(false);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(newId);

        static bool Same(string left, string right) =>
            string.Equals(Usage.ButtonUsageStore.Normalize(left), Usage.ButtonUsageStore.Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The classes a button of a plugin.json folder runs (its command, the commands its page calls) and its page.</summary>
    private static (HashSet<string> Classes, string? Page) PartsOf(ExtensionDescriptor descriptor, ExtensionButton button)
    {
        var id = descriptor.Id;
        string? page = null;
        if (string.IsNullOrWhiteSpace(button.Command))
        {
            page = !string.IsNullOrWhiteSpace(button.EntryHtml) ? button.EntryHtml : descriptor.Manifest.Ui?.EntryHtml ?? "index.html";
        }
        else if (!string.IsNullOrWhiteSpace(button.EntryHtml))
        {
            page = button.EntryHtml;
        }

        var classes = new HashSet<string>(StringComparer.Ordinal);
        if (button.Command is { } command && command.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
        {
            classes.Add(command[(id.Length + 1)..]);
        }

        if (page is not null && File.Exists(Path.Combine(descriptor.Directory, page)))
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(descriptor.Directory, page)),
                         $@"[""'`]{System.Text.RegularExpressions.Regex.Escape(id)}\.(\w+)[""'`]"))
            {
                classes.Add(match.Groups[1].Value);
            }
        }

        return (classes, page);
    }

    /// <summary>A file name in a folder that is not taken yet: <c>Name.cs</c>, else <c>Name2.cs</c>, …</summary>
    private static string FreeName(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        for (var number = 2; File.Exists(path); number++)
        {
            path = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + number + Path.GetExtension(name));
        }

        return path;
    }

    /// <summary>The folder of a panel of a tab, made (last in the layout) when it is new.</summary>
    private static string EnsurePanel(ExtensionTab tab, string panel)
    {
        var directory = ExtensionScaffold.FindChild(tab.Directory, panel, ButtonFolder.PanelSuffix);
        if (directory is null)
        {
            directory = Path.Combine(tab.Directory, ExtensionScaffold.FolderName(panel) + ButtonFolder.PanelSuffix);
            Directory.CreateDirectory(directory);
            ButtonFolder.UpdateBundle(directory, json => json["name"] = panel.Trim());
            ButtonFolder.SetLayout(tab.Directory, tab.Panels.Select(item => item.Directory).Append(directory));
        }

        return directory;
    }

    /// <summary>One of the user's roots: the authoring root when <see langword="null" />.</summary>
    private string ResolveRoot(string? root)
    {
        return root is null
            ? paths.AuthoringRoot
            : paths.GetRoots().FirstOrDefault(candidate => ExtensionPaths.SamePath(candidate.Path, root) && candidate.Zone == ExtensionZone.Dev)?.Path
              ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"'{root}' is not one of your folders of extensions.");
    }
}
