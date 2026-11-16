using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using System.ComponentModel;

namespace AnalyseTool.Extensions.Features;

/// <summary>An extension as the pages and AI clients see it.</summary>
/// <param name="Id">The id; the prefix of its command names.</param>
/// <param name="Name">The text of its first button, otherwise the id.</param>
/// <param name="Description">What it does.</param>
/// <param name="Version">The version the author gave it.</param>
/// <param name="Publisher">The author.</param>
/// <param name="Directory">The folder.</param>
/// <param name="Zone"><c>Dev</c> — the user's own; <c>Managed</c> — an installed package.</param>
/// <param name="Kind"><c>Source</c> — C# sources AnalyseTool compiles; <c>Dll</c> — a prebuilt DLL; <c>Page</c> — no commands.</param>
/// <param name="Enabled">The user has not turned it off.</param>
/// <param name="Compatible">This Revit can load its commands.</param>
/// <param name="Commands">Its commands in the pipeline.</param>
/// <param name="Buttons">The texts of its ribbon buttons.</param>
/// <param name="Errors">Why something did not load.</param>
/// <param name="Diagnostics">The compiler's errors and warnings.</param>
/// <param name="LoadedVersion">The pinned version that is loaded; <see langword="null" /> for the current files.</param>
/// <param name="UsesOlderBuild">The current sources do not compile; an earlier build is loaded.</param>
public sealed record ExtensionSummary(
    string Id,
    string Name,
    string? Description,
    string Version,
    string? Publisher,
    string Directory,
    ExtensionZone Zone,
    ExtensionKind Kind,
    bool Enabled,
    bool Compatible,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Buttons,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CompileDiagnostic> Diagnostics,
    string? LoadedVersion,
    bool UsesOlderBuild)
{
    /// <summary>Describes a tab of the user (<c>&lt;Name&gt;.tab</c>): its buttons are extensions of their own.</summary>
    public static ExtensionSummary From(ExtensionTab tab, ExtensionManager manager)
    {
        var buttons = manager.TabButtons(tab);
        return new ExtensionSummary(
            tab.Id,
            tab.Name,
            tab.Name,
            "1.0.0",
            null,
            tab.Directory,
            ExtensionZone.Dev,
            ExtensionKind.Source,
            manager.IsTabEnabled(tab.Id),
            true,
            buttons.SelectMany(state => state.Commands).ToList(),
            buttons.Select(state => state.Descriptor.Manifest.Name ?? state.Descriptor.Id).ToList(),
            buttons.SelectMany(state => state.Errors).ToList(),
            buttons.SelectMany(state => state.Diagnostics).ToList(),
            null,
            false);
    }

    /// <summary>Describes a loaded extension.</summary>
    public static ExtensionSummary From(ExtensionState state)
    {
        var manifest = state.Descriptor.Manifest;
        var buttons = manifest.Ui?.EffectiveButtons().Select(button => button.Name).ToList() ?? [];
        return new ExtensionSummary(
            manifest.Id,
            !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : buttons.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? manifest.Id,
            manifest.Description,
            manifest.Version,
            manifest.Publisher,
            state.Descriptor.Directory,
            state.Descriptor.Zone,
            state.Descriptor.Kind,
            state.Enabled,
            state.Descriptor.IsCompatible,
            state.Commands,
            buttons,
            state.Errors,
            state.Diagnostics,
            state.LoadedVersion,
            state.UsesOlderBuild);
    }
}

/// <summary>The extensions and the folders that could not be read.</summary>
/// <param name="RevitVersion">The Revit year they are loaded for.</param>
/// <param name="Extensions">Every extension found.</param>
/// <param name="BrokenFolders">Folders with a <c>plugin.json</c> that cannot be read, with the reason.</param>
public sealed record ExtensionList(string RevitVersion, IReadOnlyList<ExtensionSummary> Extensions, IReadOnlyDictionary<string, string> BrokenFolders);

/// <summary>Lists the extensions.</summary>
[McpTool(ReadOnly = true, Title = "List extensions")]
[Command(Description = "Lists the AnalyseTool extensions (ribbon buttons with C# commands or pages): id, folder, kind (Source = C# compiled by AnalyseTool, Dll, Page), commands, load errors and compiler diagnostics.")]
public sealed class GetExtensions(ExtensionHost host, ExtensionWatcher watcher) : ICommand<ExtensionList>
{
    /// <inheritdoc />
    public async Task<ExtensionList> ExecuteAsync(CommandContext context)
    {
        await watcher.FirstLoad.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        return new ExtensionList(host.RevitVersion, host.GetStates().Select(ExtensionSummary.From).ToList(), host.GetFolderProblems());
    }
}

/// <summary>Input of <see cref="ReloadExtensions" />.</summary>
/// <param name="Id">One extension; every extension when absent.</param>
public sealed record ReloadExtensionsRequest([property: Description("The id of one extension; reloads every extension when absent.")] string? Id = null);

/// <summary>Loads the extensions again.</summary>
[McpTool(Title = "Reload extensions")]
[Command(Description = "Scans the extension folders and loads them again (compiling changed C# sources). Normally not needed: a saved file reloads its extension by itself.")]
public sealed class ReloadExtensions(ExtensionHost host) : ICommand<ReloadExtensionsRequest, ExtensionList>
{
    /// <inheritdoc />
    public async Task<ExtensionList> ExecuteAsync(ReloadExtensionsRequest request, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            await host.ReloadAllAsync(context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            await host.ReloadAsync(request.Id, context.CancellationToken).ConfigureAwait(false);
        }

        return new ExtensionList(host.RevitVersion, host.GetStates().Select(ExtensionSummary.From).ToList(), host.GetFolderProblems());
    }
}

/// <summary>Input of <see cref="CreateExtension" />.</summary>
public sealed record CreateExtensionRequest(
    [property: Description("The text of the ribbon button, e.g. «Count walls».")] string Name,
    [property: Description("What the button does; becomes the tooltip and the command description.")] string? Description = null,
    [property: Description("The id; derived from the name when absent. Letters, digits, '.', '-', '_'.")] string? Id = null,
    [property: Description("CommandWithPage (default): a button that opens an HTML page calling its C# command; Command: a button that runs C# directly; Page: a page without C#.")] ExtensionTemplate Template = ExtensionTemplate.CommandWithPage,
    [property: Description("The panel of the tab; the first panel of the tab, or the tab name, when absent.")] string? Panel = null,
    [property: Description("One of the user's folders of extensions; the authoring folder when absent.")] string? Root = null,
    [property: Description("The tab: the id of one (e.g. «sheets.tab») or a name; a tab of that name is made when there is none. The button's name when absent.")] string? Tab = null);

/// <summary>Creates a new extension: C# sources by default, with the first version saved.</summary>
[McpTool(Title = "Create extension")]
[Command(Description = "Creates a new ribbon button: a folder <Tab>.tab\\<Panel>.panel\\<Name>.pushbutton\\ with bundle.json and a C# file that AnalyseTool compiles itself (no project, no DLL), AGENTS.md and CLAUDE.md that point to the authoring guide (LLM.md next to the add-in), and its own first saved version. Returns the button (an extension of its own, id <tab>.<button>) with its folder; then edit its *.cs with WriteExtensionFile.")]
public sealed class CreateExtension(ExtensionManager manager) : ICommand<CreateExtensionRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(CreateExtensionRequest request, CommandContext context)
    {
        var state = await manager.CreateAsync(request.Name, request.Description, request.Template, request.Panel, request.Root, request.Tab, context.CancellationToken).ConfigureAwait(false);
        return ExtensionSummary.From(state);
    }
}

/// <summary>Input of <see cref="CreateExtensionFolder" />.</summary>
public sealed record CreateExtensionFolderRequest(
    [property: Description("The name of the folder, e.g. «Sheets».")] string Name,
    [property: Description("What its buttons are for.")] string? Description = null,
    [property: Description("The id; derived from the name when absent. Letters, digits, '.', '-', '_'.")] string? Id = null,
    [property: Description("The ribbon panel of its buttons; «Extensions» when absent.")] string? Panel = null,
    [property: Description("The ribbon tab of its buttons; the AnalyseTool tab when absent.")] string? Tab = null,
    [property: Description("One of the user's folders of extensions; the authoring folder when absent.")] string? Root = null);

/// <summary>Creates an empty folder of buttons.</summary>
[McpTool(Title = "Create a folder of buttons")]
[Command(Description = "Creates an empty folder of buttons — a ribbon tab: <Name>.tab\\ with bundle.json. Its id is «<name>.tab». Add buttons with AddExtensionButton.")]
public sealed class CreateExtensionFolder(ExtensionManager manager) : ICommand<CreateExtensionFolderRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(CreateExtensionFolderRequest request, CommandContext context)
    {
        var tab = await manager.CreateTabAsync(request.Name, request.Root, context.CancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(request.Panel))
        {
            tab = await manager.SetTabPanelAsync(tab.Id, null, request.Panel, context.CancellationToken).ConfigureAwait(false);
        }

        return ExtensionSummary.From(tab, manager);
    }
}

/// <summary>Input of <see cref="AddExtensionButton" />.</summary>
public sealed record AddExtensionButtonRequest(
    [property: Description("The id of the folder (extension).")] string Id,
    [property: Description("The text of the button, e.g. «Renumber sheets».")] string Name,
    [property: Description("What the button does: its tooltip and the description of its command.")] string? Description = null,
    [property: Description("CommandWithPage (default): opens a page that calls its C# command; Command: runs C# directly; Page: a page without C#.")] ExtensionTemplate Template = ExtensionTemplate.CommandWithPage,
    [property: Description("Its own ribbon tab; the folder's when absent.")] string? Tab = null,
    [property: Description("Its own ribbon panel; the folder's when absent.")] string? Panel = null);

/// <summary>Adds a button to a folder.</summary>
[McpTool(Title = "Add a button to a folder")]
[Command(Description = "Adds a button to one of the user's folders: its C# command in src\\<Name>.cs (and/or a page) and its entry in plugin.json, saved as a version. Returns the folder; then edit the new src\\*.cs with WriteExtensionFile.")]
public sealed class AddExtensionButton(ExtensionManager manager) : ICommand<AddExtensionButtonRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(AddExtensionButtonRequest request, CommandContext context)
    {
        var state = ExtensionManager.IsTab(request.Id)
            ? await manager.AddTabButtonAsync(request.Id, request.Name, request.Description, request.Template, request.Panel, context.CancellationToken).ConfigureAwait(false)
            : await manager.AddButtonAsync(request.Id, request.Name, request.Description, request.Template, context.CancellationToken, request.Tab, request.Panel).ConfigureAwait(false);
        return ExtensionSummary.From(state);
    }
}

/// <summary>Input of <see cref="UpdateExtensionFolder" />.</summary>
public sealed record UpdateExtensionFolderRequest(
    [property: Description("The id of the folder (extension).")] string Id,
    [property: Description("Its new name.")] string Name,
    [property: Description("What its buttons are for.")] string? Description = null,
    [property: Description("The ribbon tab of its buttons: empty for the AnalyseTool tab, absent to keep it.")] string? Tab = null,
    [property: Description("The ribbon panel of its buttons: empty for «Extensions», absent to keep it.")] string? Panel = null);

/// <summary>Renames a folder.</summary>
[McpTool(Title = "Rename a folder of buttons")]
[Command(Description = "Changes the name and description of one of the user's folders (plugin.json), saved as a version. The id stays.")]
public sealed class UpdateExtensionFolder(ExtensionManager manager) : ICommand<UpdateExtensionFolderRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(UpdateExtensionFolderRequest request, CommandContext context)
    {
        if (ExtensionManager.IsTab(request.Id))
        {
            return ExtensionSummary.From(await manager.RenameTabAsync(request.Id, request.Name, context.CancellationToken).ConfigureAwait(false), manager);
        }

        return ExtensionSummary.From(await manager.UpdateFolderAsync(request.Id, request.Name, request.Description, request.Tab, request.Panel, context.CancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Input of <see cref="UpdateExtensionButton" />.</summary>
public sealed record UpdateExtensionButtonRequest(
    [property: Description("The id of the folder (extension).")] string Id,
    [property: Description("The current text of the button.")] string Button,
    [property: Description("Its new text.")] string Name,
    [property: Description("Its tooltip: what it does.")] string? Tooltip = null,
    [property: Description("Its own ribbon tab: empty for the folder's, absent to keep it.")] string? Tab = null,
    [property: Description("Its own ribbon panel: empty for the folder's, absent to keep it.")] string? Panel = null,
    [property: Description("Its page opens inside the pane (a small interface) instead of a window; absent to keep it.")] bool? Dockable = null);

/// <summary>Renames a button.</summary>
[McpTool(Title = "Rename a button")]
[Command(Description = "Changes the text and tooltip of a button in plugin.json, saved as a version. Its command and code keep their names.")]
public sealed class UpdateExtensionButton(ExtensionManager manager) : ICommand<UpdateExtensionButtonRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(UpdateExtensionButtonRequest request, CommandContext context)
    {
        var id = request.Id;
        if (ExtensionManager.IsTab(id))
        {
            // A button of a tab is an extension of its own
            var tab = manager.GetTab(id);
            id = manager.TabButtons(tab).FirstOrDefault(state => (state.Descriptor.Manifest.Ui?.EffectiveButtons() ?? []).SelectMany(button => (button.Items ?? []).Prepend(button))
                     .Any(button => string.Equals(Usage.ButtonUsageStore.Normalize(button.Name), Usage.ButtonUsageStore.Normalize(request.Button), StringComparison.OrdinalIgnoreCase)))?.Descriptor.Id
                 ?? throw new CommandException(ExtensionErrorCodes.Invalid, $"The tab «{tab.Name}» has no button «{request.Button}».");
        }

        return ExtensionSummary.From(await manager.UpdateButtonAsync(id, request.Button, request.Name, request.Tooltip, request.Tab, request.Panel, context.CancellationToken, request.Dockable).ConfigureAwait(false));
    }
}

/// <summary>Input of <see cref="SetExtensionPanel" />.</summary>
public sealed record SetExtensionPanelRequest(
    [property: Description("The id of the folder (extension).")] string Id,
    [property: Description("The panel to rename; absent to add a new panel.")] string? Panel,
    [property: Description("The (new) name of the panel.")] string Name);

/// <summary>Adds or renames a panel of a folder.</summary>
[McpTool(Title = "Add or rename a panel of a folder")]
[Command(Description = "Adds a panel (a section of the folder, a panel of its ribbon tab) or renames one — its buttons follow. Saved as a version.")]
public sealed class SetExtensionPanel(ExtensionManager manager) : ICommand<SetExtensionPanelRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(SetExtensionPanelRequest request, CommandContext context)
    {
        if (ExtensionManager.IsTab(request.Id))
        {
            return ExtensionSummary.From(await manager.SetTabPanelAsync(request.Id, request.Panel, request.Name, context.CancellationToken).ConfigureAwait(false), manager);
        }

        return ExtensionSummary.From(await manager.SetPanelAsync(request.Id, request.Panel, request.Name, context.CancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Input of <see cref="MoveExtensionButton" />.</summary>
public sealed record MoveExtensionButtonRequest(
    [property: Description("The id of the folder the button is in.")] string Id,
    [property: Description("The text of the button.")] string Button,
    [property: Description("The panel it goes to.")] string Panel,
    [property: Description("The button it goes before; the end of the panel when absent.")] string? Before = null,
    [property: Description("Another folder of the user it moves to; the same folder when absent.")] string? ToId = null);

/// <summary>Moves a button to another panel or folder.</summary>
[McpTool(Title = "Move a button")]
[Command(Description = "Moves a button to another panel of its folder, or to another folder of the user — its C# file and page go along and its command is renamed to the new folder. Saved as versions.")]
public sealed class MoveExtensionButton(ExtensionManager manager) : ICommand<MoveExtensionButtonRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(MoveExtensionButtonRequest request, CommandContext context)
    {
        var toTab = request.ToId is { Length: > 0 } to && ExtensionManager.IsTab(to);
        if (ExtensionManager.IsTab(request.Id) != (toTab || (string.IsNullOrWhiteSpace(request.ToId) && ExtensionManager.IsTab(request.Id))))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A button moves between tabs, or between folders of the old layout (plugin.json) — not from one to the other yet.");
        }

        if (ExtensionManager.IsTab(request.Id))
        {
            return ExtensionSummary.From(await manager.MoveTabButtonAsync(request.Id, request.Button, request.Panel, request.Before, request.ToId, context.CancellationToken).ConfigureAwait(false));
        }

        return ExtensionSummary.From(await manager.MoveButtonAsync(request.Id, request.Button, request.Panel, request.Before, request.ToId, context.CancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Input of commands that act on one extension.</summary>
/// <param name="Id">The id of the extension.</param>
public sealed record ExtensionRequest([property: Description("The id of the extension.")] string Id);

/// <summary>Input of <see cref="SetExtensionEnabled" />.</summary>
public sealed record SetExtensionEnabledRequest(
    [property: Description("The id of the extension.")] string Id,
    [property: Description("true to load it, false to keep it listed but unloaded.")] bool Enabled);

/// <summary>Turns an extension on or off.</summary>
[McpTool(Title = "Turn an extension on or off")]
[Command(Description = "Turns an extension on or off. Off: it stays listed, its buttons and commands go.")]
public sealed class SetExtensionEnabled(ExtensionManager manager) : ICommand<SetExtensionEnabledRequest, ExtensionSummary?>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary?> ExecuteAsync(SetExtensionEnabledRequest request, CommandContext context)
    {
        if (ExtensionManager.IsTab(request.Id))
        {
            return ExtensionSummary.From(await manager.SetTabEnabledAsync(request.Id, request.Enabled, context.CancellationToken).ConfigureAwait(false), manager);
        }

        var state = await manager.SetEnabledAsync(request.Id, request.Enabled, context.CancellationToken).ConfigureAwait(false);
        return state is null ? null : ExtensionSummary.From(state);
    }
}

/// <summary>The result of <see cref="RemoveExtension" />.</summary>
/// <param name="MovedTo">Where the folder went, with its history.</param>
public sealed record RemoveExtensionResult(string MovedTo);

/// <summary>Removes an extension: its folder moves to the trash of the profile. Not for AI clients.</summary>
[Command(Description = "Removes an extension: it leaves the ribbon and the pipeline, its folder (with its history) moves to %LOCALAPPDATA%\\AnalyseTool\\trash.")]
public sealed class RemoveExtension(ExtensionManager manager) : ICommand<ExtensionRequest, RemoveExtensionResult>
{
    /// <inheritdoc />
    public async Task<RemoveExtensionResult> ExecuteAsync(ExtensionRequest request, CommandContext context)
    {
        return new RemoveExtensionResult(ExtensionManager.IsTab(request.Id)
            ? await manager.RemoveTabAsync(request.Id).ConfigureAwait(false)
            : await manager.RemoveAsync(request.Id).ConfigureAwait(false));
    }
}

/// <summary>Input of <see cref="InstallExtensionPackage" />.</summary>
public sealed record InstallExtensionPackageRequest(
    [property: Description("The full path of the .zip package.")] string Path,
    [property: Description("Replace an installed version.")] bool Overwrite = false);

/// <summary>Installs an extension package (.zip). Not for AI clients: it installs code.</summary>
[Command(Description = "Installs an extension package (.zip with plugin.json) into extensions-dist; an update keeps the history of the folder, so the previous version can be restored.")]
public sealed class InstallExtensionPackage(ExtensionManager manager) : ICommand<InstallExtensionPackageRequest, PackageInstallResult>
{
    /// <inheritdoc />
    public Task<PackageInstallResult> ExecuteAsync(InstallExtensionPackageRequest request, CommandContext context)
    {
        return manager.InstallPackageAsync(request.Path, request.Overwrite, context.CancellationToken);
    }
}

/// <summary>The result of <see cref="BuildExtension" />.</summary>
/// <param name="Success">The sources compile.</param>
/// <param name="Diagnostics">The errors and warnings.</param>
/// <param name="Extension">The extension after it was loaded with the new build.</param>
public sealed record BuildExtensionResult(bool Success, IReadOnlyList<CompileDiagnostic> Diagnostics, ExtensionSummary? Extension);

/// <summary>Compiles the sources of an extension and loads the result.</summary>
[McpTool(Title = "Build extension")]
[Command(Description = "Compiles the C# sources (src\\*.cs) of an extension against the running Revit and loads the result. Returns the compiler errors with file, line and column — the way to check code before clicking the button.")]
public sealed class BuildExtension(ExtensionManager manager) : ICommand<ExtensionRequest, BuildExtensionResult>
{
    /// <inheritdoc />
    public async Task<BuildExtensionResult> ExecuteAsync(ExtensionRequest request, CommandContext context)
    {
        var state = manager.Get(request.Id);
        if (state.Descriptor.Kind != ExtensionKind.Source)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{request.Id}' is not built from sources ({state.Descriptor.Kind}).");
        }

        var build = ExtensionBuilder.Build(state.Descriptor, manager.Host.RevitVersion);
        var reloaded = await manager.Host.ReloadAsync(request.Id, context.CancellationToken).ConfigureAwait(false);
        return new BuildExtensionResult(!build.Failed, build.Diagnostics, reloaded is null ? null : ExtensionSummary.From(reloaded));
    }
}

/// <summary>The authoring guide.</summary>
/// <param name="Markdown">LLM.md: the Sdk contract, the manifest, pages and the AT bridge.</param>
/// <param name="Path">Where LLM.md lies: next to the add-in, updated with it.</param>
public sealed record AuthoringGuide(string Markdown, string Path);

/// <summary>Returns the authoring guide.</summary>
[McpTool(ReadOnly = true, Title = "Extension authoring guide")]
[Command(Description = "Returns LLM.md, the guide to writing AnalyseTool extensions: the IRevitTask contract of AnalyseTool.Sdk, plugin.json, pages and AT.invoke, and the path of the file next to the add-in. Read it before writing extension code.")]
public sealed class GetAuthoringGuide : ICommand<AuthoringGuide>
{
    /// <inheritdoc />
    public Task<AuthoringGuide> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new AuthoringGuide(ExtensionScaffold.Guide, ExtensionScaffold.GuidePath));
    }
}
