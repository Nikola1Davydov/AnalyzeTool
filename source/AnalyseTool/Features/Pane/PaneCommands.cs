using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Usage;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Infrastructure.Ribbon;

namespace AnalyseTool.Features.Pane;

// The dock pane: every button of the ribbon as a tile — AnalyseTool's own and the extensions' — with the user's profile
// (favourites in the dock, tile size, order, theme) and the statistics. A tap goes through the same ButtonRunner as a
// ribbon click, so it runs the same command with the same args and is counted the same way.

/// <summary>A button as the pane shows it.</summary>
/// <param name="Key">Its key: <c>&lt;extension&gt;/&lt;name&gt;</c> — the key of its statistics and of the profile.</param>
/// <param name="Name">The text, line breaks as spaces.</param>
/// <param name="Tooltip">What it does.</param>
/// <param name="Glyph">A Segoe MDL2 Assets character, when the icon is <c>glyph:XXXX</c>.</param>
/// <param name="Image">A PNG icon of the folder as a data URL.</param>
/// <param name="Command">The command it runs; <see langword="null" /> when it opens a page.</param>
/// <param name="Group">The pulldown it sits in on the ribbon: a folder in the pane.</param>
/// <param name="Tab">Its own ribbon tab (plugin.json), when it has one.</param>
/// <param name="Panel">Its own ribbon panel, when it has one.</param>
public sealed record PaneButton(string Key, string Name, string? Tooltip, string? Glyph, string? Image, string? Command, string? Group, string? Tab = null, string? Panel = null);

/// <summary>A folder of buttons: AnalyseTool itself, or an extension.</summary>
/// <param name="Id">The folder id.</param>
/// <param name="Name">The description of the extension, or its id.</param>
/// <param name="BuiltIn">AnalyseTool's own buttons.</param>
/// <param name="Enabled">The extension is on (its buttons work).</param>
/// <param name="Problems">Load errors and compiler errors, as <c>file(line): message</c>.</param>
/// <param name="Buttons">Its clickable buttons.</param>
/// <param name="CanAddButtons">One of the user's folders: «+ New button» adds C# or a page to it (not AnalyseTool, not a package, not a DLL).</param>
/// <param name="Description">What its buttons are for.</param>
/// <param name="Directory">The folder on disk, to open in Explorer or an AI tool; <see langword="null" /> for AnalyseTool's own.</param>
/// <param name="Tab">The ribbon tab of its buttons (plugin.json <c>ui.tab</c>); the AnalyseTool tab when absent.</param>
/// <param name="Panel">The ribbon panel of its buttons (<c>ui.panel</c>); «Extensions» when absent.</param>
public sealed record PaneFolder(string Id, string Name, bool BuiltIn, bool Enabled, IReadOnlyList<string> Problems, IReadOnlyList<PaneButton> Buttons, bool CanAddButtons,
    string? Description = null, string? Directory = null, string? Tab = null, string? Panel = null);

/// <summary>Everything the pane shows.</summary>
/// <param name="UserName">The Windows user, for the profile.</param>
/// <param name="MachineName">This computer.</param>
/// <param name="ProfileFile">Where the profile is kept.</param>
/// <param name="Folders">AnalyseTool first, then the extensions with buttons.</param>
public sealed record PaneOverview(string UserName, string MachineName, string ProfileFile, IReadOnlyList<PaneFolder> Folders);

/// <summary>Returns the buttons of the dock pane.</summary>
[Command(Description = "Returns every ribbon button for the dock pane, grouped by AnalyseTool and the extensions.")]
public sealed class GetPaneButtons(ButtonCatalog catalog, ExtensionPaths paths) : ICommand<PaneOverview>
{
    private const int MaxImageBytes = 256 * 1024;

    /// <inheritdoc />
    public Task<PaneOverview> ExecuteAsync(CommandContext context)
    {
        var folders = catalog.GetSources()
            .Select(source => new PaneFolder(
                source.Extension.Id,
                source.BuiltIn ? "AnalyseTool" : FolderName(source.Extension),
                source.BuiltIn,
                source.Shown,
                Problems(source.State),
                Buttons(source.Extension).ToList(),
                !source.BuiltIn && source.Extension.Zone == ExtensionZone.Dev && source.Extension.Kind != ExtensionKind.Dll,
                source.Extension.Manifest.Description,
                source.BuiltIn ? null : source.Extension.Directory,
                source.Extension.Manifest.Ui?.Tab,
                source.Extension.Manifest.Ui?.Panel))
            .ToList();
        return Task.FromResult(new PaneOverview(Environment.UserName, Environment.MachineName, PaneProfile.FilePath(paths), folders));
    }

    /// <summary>The name of a folder: its <c>name</c>, else its description, else its id.</summary>
    internal static string FolderName(ExtensionDescriptor extension)
    {
        var manifest = extension.Manifest;
        return !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : !string.IsNullOrWhiteSpace(manifest.Description) ? manifest.Description : extension.Id;
    }

    private static IReadOnlyList<string> Problems(ExtensionState? state)
    {
        if (state is null)
        {
            return [];
        }

        return state.Errors
            .Concat(state.Diagnostics.Where(diagnostic => diagnostic.Severity == "error").Select(diagnostic => $"{diagnostic.File}({diagnostic.Line}): {diagnostic.Message}"))
            .ToList();
    }

    private static IEnumerable<PaneButton> Buttons(ExtensionDescriptor extension)
    {
        // "pane": false — a button of the ribbon only
        foreach (var button in (extension.Manifest.Ui?.EffectiveButtons() ?? []).Where(button => button.InPane))
        {
            if (button.ResolvedKind == ButtonKind.Pulldown)
            {
                foreach (var child in (button.Items ?? []).Where(child => child.InPane))
                {
                    yield return Create(extension, child, ButtonUsageStore.Normalize(button.Name));
                }
            }
            else
            {
                yield return Create(extension, button, null);
            }
        }
    }

    private static PaneButton Create(ExtensionDescriptor extension, ExtensionButton button, string? group)
    {
        var (glyph, image) = Icon(extension.Directory, button.Icon);
        return new PaneButton(ButtonUsageStore.Key(extension.Id, button.Name), ButtonUsageStore.Normalize(button.Name), button.Tooltip, glyph, image,
            string.IsNullOrWhiteSpace(button.Command) ? null : button.Command, group, button.Tab, button.Panel);
    }

    /// <summary>A glyph character, or a PNG of the folder as a data URL — never a file outside the folder.</summary>
    internal static (string? Glyph, string? Image) Icon(string directory, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return (null, null);
        }

        if (icon.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(icon["glyph:".Length..].Trim().TrimStart('U', 'u', '+'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint) && codePoint is > 0 and <= 0x10FFFF
                ? (char.ConvertFromUtf32(codePoint), null)
                : (null, null);
        }

        try
        {
            var root = Path.GetFullPath(directory);
            var path = Path.GetFullPath(Path.Combine(root, icon));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length > MaxImageBytes)
            {
                return (null, null);
            }

            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                _ => null
            };
            return type is null ? (null, null) : (null, $"data:{type};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }
}

/// <summary>
///     The «Refresh» button of the ribbon and of the pane: reads every extension folder again, compiles what changed and
///     rebuilds the ribbon. Saved files reload by themselves; this is for what the watcher cannot see (a network folder, a
///     folder copied in while Revit was busy). Shows nothing when it worked — the buttons themselves are the answer.
/// </summary>
[Command(Description = "Reads every extension folder again, compiles what changed and rebuilds the ribbon and the pane.")]
public sealed class RefreshButtons(ExtensionHost host) : ICommand<Empty>
{
    /// <inheritdoc />
    public async Task<Empty> ExecuteAsync(CommandContext context)
    {
        await host.ReloadAllAsync(context.CancellationToken).ConfigureAwait(false);
        return Empty.Value;
    }
}

/// <summary>Input of <see cref="RunButton" />.</summary>
public sealed record RunButtonRequest([property: Description("The key of the button: <extension>/<name>.")] string Key);

/// <summary>Runs a button from the dock pane: what a click on the ribbon does, the result coming back instead of a dialog.</summary>
[Command(Description = "Clicks a ribbon button: runs its command with its args, or opens its page, and counts the click.")]
public sealed class RunButton(ButtonCatalog catalog, ButtonRunner runner) : ICommand<RunButtonRequest, ButtonRunResult>
{
    /// <inheritdoc />
    public Task<ButtonRunResult> ExecuteAsync(RunButtonRequest request, CommandContext context)
    {
        var entry = catalog.Find(request.Key) ?? throw new CommandException(CommandErrorCodes.NotFound, $"There is no button '{request.Key}'.");
        if (!entry.Source.Shown)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"The extension '{entry.Source.Extension.Id}' is turned off.");
        }

        return runner.RunAsync(entry.Source.Extension, entry.Button, action => Infrastructure.App.Ui.InvokeAsync(() =>
        {
            action();
            return true;
        }));
    }
}

/// <summary>The profile of the pane: favourites, tile size, order, theme. Kept in the AnalyseTool profile folder.</summary>
/// <param name="Profile">The profile as the pane wrote it; <see langword="null" /> before the first save.</param>
public sealed record PaneProfileResult(JsonElement? Profile);

/// <summary>Input of <see cref="SavePaneProfile" />.</summary>
public sealed record SavePaneProfileRequest([property: Description("The whole profile, as the pane keeps it.")] JsonElement Profile);

/// <summary>Reads the profile of the pane.</summary>
[Command(Description = "Returns the profile of the dock pane: the favourite buttons, the tile size, the order and the theme.")]
public sealed class GetPaneProfile(ExtensionPaths paths) : ICommand<PaneProfileResult>
{
    /// <inheritdoc />
    public Task<PaneProfileResult> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new PaneProfileResult(PaneProfile.Read(paths)));
    }
}

/// <summary>Saves the profile of the pane.</summary>
[Command(Description = "Saves the profile of the dock pane; its starred buttons (\"ribbon\") go on the Revit ribbon.")]
public sealed class SavePaneProfile(ExtensionPaths paths, RibbonFavorites favorites) : ICommand<SavePaneProfileRequest, Empty>
{
    /// <inheritdoc />
    public Task<Empty> ExecuteAsync(SavePaneProfileRequest request, CommandContext context)
    {
        if (request.Profile.ValueKind != JsonValueKind.Object)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "The profile must be a JSON object.");
        }

        PaneProfile.Write(paths, request.Profile);
        // The stars of the profile are buttons of the Revit ribbon: it rebuilds when they change
        favorites.Update(request.Profile);
        return Task.FromResult(Empty.Value);
    }
}
