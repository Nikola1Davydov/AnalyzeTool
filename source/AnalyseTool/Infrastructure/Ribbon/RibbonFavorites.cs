using System.Text.Json;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     The buttons the user starred in the pane: they go on the Revit ribbon, in the «Favourites» panel of the AnalyseTool tab,
///     whatever their <c>"ribbon"</c> says. The list is the <c>ribbon</c> array of the pane profile (<see cref="PaneProfile" />):
///     one file, one owner — the pane writes it, the ribbon follows.
/// </summary>
public sealed class RibbonFavorites(ExtensionPaths paths)
{
    /// <summary>The name of the array in the pane profile.</summary>
    public const string ProfileProperty = "ribbon";

    private readonly Lock _gate = new();
    private IReadOnlyList<string>? _keys;

    /// <summary>Raised when the starred buttons changed; the ribbon rebuilds.</summary>
    public event EventHandler? Changed;

    /// <summary>The keys of the starred buttons (<c>&lt;extension&gt;/&lt;name&gt;</c>), in the order of the panel.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return _keys ??= Parse(PaneProfile.Read(paths));
            }
        }
    }

    /// <summary>Takes the starred buttons of a profile the pane just saved; raises <see cref="Changed" /> when they differ.</summary>
    public void Update(JsonElement profile)
    {
        var keys = Parse(profile);
        lock (_gate)
        {
            if (_keys is not null && _keys.SequenceEqual(keys, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _keys = keys;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The <c>ribbon</c> array of a profile: strings, no duplicates, in order.</summary>
    public static IReadOnlyList<string> Parse(JsonElement? profile)
    {
        if (profile is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty(ProfileProperty, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
