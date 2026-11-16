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
    /// <summary>Describes a loaded extension.</summary>
    public static ExtensionSummary From(ExtensionState state)
    {
        var manifest = state.Descriptor.Manifest;
        var buttons = manifest.Ui?.EffectiveButtons().Select(button => button.Name).ToList() ?? [];
        return new ExtensionSummary(
            manifest.Id,
            buttons.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? manifest.Id,
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
    [property: Description("Command (default): a button that runs C#; CommandWithPage: a button that opens an HTML page calling C#; Page: a page without C#.")] ExtensionTemplate Template = ExtensionTemplate.Command,
    [property: Description("The ribbon panel; «Extensions» when absent.")] string? Panel = null,
    [property: Description("One of the user's folders of extensions; the authoring folder when absent.")] string? Root = null);

/// <summary>Creates a new extension: C# sources by default, with the first version saved.</summary>
[McpTool(Title = "Create extension")]
[Command(Description = "Creates a new ribbon button as an extension: a folder with plugin.json and C# sources in src\\ that AnalyseTool compiles itself (no project, no DLL), LLM.md with the authoring guide, and the first saved version. Returns the extension with its folder; then edit src\\*.cs with WriteExtensionFile.")]
public sealed class CreateExtension(ExtensionManager manager) : ICommand<CreateExtensionRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(CreateExtensionRequest request, CommandContext context)
    {
        var state = await manager.CreateAsync(request.Name, request.Id, request.Description, request.Template, request.Panel, request.Root, context.CancellationToken).ConfigureAwait(false);
        return ExtensionSummary.From(state);
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
        return new RemoveExtensionResult(await manager.RemoveAsync(request.Id).ConfigureAwait(false));
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
public sealed record AuthoringGuide(string Markdown);

/// <summary>Returns the authoring guide.</summary>
[McpTool(ReadOnly = true, Title = "Extension authoring guide")]
[Command(Description = "Returns LLM.md, the guide to writing AnalyseTool extensions: the IRevitTask contract of AnalyseTool.Sdk, plugin.json, pages and AT.invoke. Read it before writing extension code.")]
public sealed class GetAuthoringGuide : ICommand<AuthoringGuide>
{
    /// <inheritdoc />
    public Task<AuthoringGuide> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new AuthoringGuide(ExtensionScaffold.Guide));
    }
}
