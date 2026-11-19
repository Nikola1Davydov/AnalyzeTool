using System.Text.Json;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     What the user keeps off the Revit ribbon, from the «+» of the pane: whole folders (their tab) and single buttons. They
///     stay tiles of the pane. Kept in the pane profile (<see cref="PaneProfile" />, <c>hiddenFolders</c> and <c>hiddenButtons</c>):
///     a choice of this user, not of the folder — on a shared folder everyone decides for themselves.
/// </summary>
public sealed class RibbonVisibility(ExtensionPaths paths)
{
    /// <summary>The array of the folder ids whose tab is off.</summary>
    public const string HiddenFoldersProperty = "hiddenFolders";

    /// <summary>The array of the button keys (<c>&lt;folder&gt;/&lt;name&gt;</c>) not loaded on the ribbon.</summary>
    public const string HiddenButtonsProperty = "hiddenButtons";

    /// <summary>The array of the button keys loaded although their plugin.json says <c>"ribbon": false</c>.</summary>
    public const string ShownButtonsProperty = "ribbon";

    private readonly Lock _gate = new();
    private (HashSet<string> Folders, HashSet<string> Buttons, HashSet<string> Shown)? _hidden;

    /// <summary>Raised when what is hidden changed; the ribbon rebuilds.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether the tab of a folder is off.</summary>
    public bool IsFolderHidden(string folder)
    {
        return Hidden().Folders.Contains(folder);
    }

    /// <summary>Whether a button goes on the ribbon: as its plugin.json says, unless the «+» of the pane says otherwise.</summary>
    public bool IsButtonLoaded(string key, bool onRibbonByDefault)
    {
        var (_, hidden, shown) = Hidden();
        return onRibbonByDefault ? !hidden.Contains(key) : shown.Contains(key);
    }

    /// <summary>A text that changes whenever what is hidden changes: part of the signature of the ribbon.</summary>
    public string Signature()
    {
        var (folders, buttons, shown) = Hidden();
        return string.Join("|", new[] { folders, buttons, shown }.Select(set => string.Join(",", set.Order(StringComparer.OrdinalIgnoreCase))));
    }

    /// <summary>Takes what a profile the pane just saved hides; raises <see cref="Changed" /> when it differs.</summary>
    public void Update(JsonElement profile)
    {
        var next = Parse(profile);
        lock (_gate)
        {
            if (_hidden is { } current && current.Folders.SetEquals(next.Folders) && current.Buttons.SetEquals(next.Buttons) && current.Shown.SetEquals(next.Shown))
            {
                return;
            }

            _hidden = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The hidden folders, the hidden buttons and the buttons shown against their plugin.json, of a profile.</summary>
    public static (HashSet<string> Folders, HashSet<string> Buttons, HashSet<string> Shown) Parse(JsonElement? profile)
    {
        return (Strings(profile, HiddenFoldersProperty), Strings(profile, HiddenButtonsProperty), Strings(profile, ShownButtonsProperty));
    }

    private (HashSet<string> Folders, HashSet<string> Buttons, HashSet<string> Shown) Hidden()
    {
        lock (_gate)
        {
            return _hidden ??= Parse(PaneProfile.Read(paths));
        }
    }

    private static HashSet<string> Strings(JsonElement? profile, string property)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (profile is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    result.Add(item.GetString()!);
                }
            }
        }

        return result;
    }
}
