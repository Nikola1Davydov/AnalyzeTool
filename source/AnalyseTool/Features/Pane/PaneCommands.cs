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
public sealed record PaneButton(string Key, string Name, string? Tooltip, string? Glyph, string? Image, string? Command, string? Group);

/// <summary>A folder of buttons: AnalyseTool itself, or an extension.</summary>
/// <param name="Id">The folder id.</param>
/// <param name="Name">The description of the extension, or its id.</param>
/// <param name="BuiltIn">AnalyseTool's own buttons.</param>
/// <param name="Enabled">The extension is on (its buttons work).</param>
/// <param name="Problems">Load errors and compiler errors, as <c>file(line): message</c>.</param>
/// <param name="Buttons">Its clickable buttons.</param>
public sealed record PaneFolder(string Id, string Name, bool BuiltIn, bool Enabled, IReadOnlyList<string> Problems, IReadOnlyList<PaneButton> Buttons);

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
                source.BuiltIn ? "AnalyseTool" : source.Extension.Manifest.Description ?? source.Extension.Id,
                source.BuiltIn,
                source.Shown,
                Problems(source.State),
                Buttons(source.Extension).ToList()))
            .ToList();
        return Task.FromResult(new PaneOverview(Environment.UserName, Environment.MachineName, PaneProfile.FilePath(paths), folders));
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
        foreach (var button in extension.Manifest.Ui!.EffectiveButtons())
        {
            if (button.ResolvedKind == ButtonKind.Pulldown)
            {
                foreach (var child in button.Items ?? [])
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
            string.IsNullOrWhiteSpace(button.Command) ? null : button.Command, group);
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
[Command(Description = "Saves the profile of the dock pane.")]
public sealed class SavePaneProfile(ExtensionPaths paths) : ICommand<SavePaneProfileRequest, Empty>
{
    /// <inheritdoc />
    public Task<Empty> ExecuteAsync(SavePaneProfileRequest request, CommandContext context)
    {
        if (request.Profile.ValueKind != JsonValueKind.Object)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "The profile must be a JSON object.");
        }

        PaneProfile.Write(paths, request.Profile);
        return Task.FromResult(Empty.Value);
    }
}

/// <summary>
///     <c>%LOCALAPPDATA%\AnalyseTool\pane-profile.json</c>: a file, not the browser storage of WebView2, so it is the same in every
///     Revit version, survives a reset of WebView2 and moves with the rest of the profile folder.
/// </summary>
internal static class PaneProfile
{
    private static readonly Lock Gate = new();

    public static string FilePath(ExtensionPaths paths)
    {
        return Path.Combine(paths.ProfileRoot, "pane-profile.json");
    }

    public static JsonElement? Read(ExtensionPaths paths)
    {
        lock (Gate)
        {
            try
            {
                var path = FilePath(paths);
                return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone() : null;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A broken profile starts the pane with the defaults; the next save writes a good one
                return null;
            }
        }
    }

    public static void Write(ExtensionPaths paths, JsonElement profile)
    {
        lock (Gate)
        {
            var path = FilePath(paths);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
    }
}
