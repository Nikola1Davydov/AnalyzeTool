using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Building;
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

        var panelDirectory = ExtensionScaffold.FindChild(target.Directory, panelName, ButtonFolder.PanelSuffix);
        if (panelDirectory is null)
        {
            panelDirectory = Path.Combine(target.Directory, ExtensionScaffold.FolderName(panelName) + ButtonFolder.PanelSuffix);
            Directory.CreateDirectory(panelDirectory);
            ButtonFolder.UpdateBundle(panelDirectory, json => json["name"] = panelName);
            ButtonFolder.SetLayout(target.Directory, target.Panels.Select(item => item.Directory).Append(panelDirectory));
        }

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

    /// <summary>One of the user's roots: the authoring root when <see langword="null" />.</summary>
    private string ResolveRoot(string? root)
    {
        return root is null
            ? paths.AuthoringRoot
            : paths.GetRoots().FirstOrDefault(candidate => ExtensionPaths.SamePath(candidate.Path, root) && candidate.Zone == ExtensionZone.Dev)?.Path
              ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"'{root}' is not one of your folders of extensions.");
    }
}
