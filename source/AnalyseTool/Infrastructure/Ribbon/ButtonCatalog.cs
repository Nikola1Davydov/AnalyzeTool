using System.IO;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>A folder whose <c>plugin.json</c> declares ribbon buttons, and whether it is AnalyseTool's own.</summary>
/// <param name="Extension">The folder and its manifest.</param>
/// <param name="BuiltIn">The buttons of AnalyseTool itself (<c>Ribbon\analysetool\</c> next to the add-in).</param>
/// <param name="Shown">Its buttons are on the ribbon: built-in, or an extension that is on.</param>
/// <param name="State">The loaded state of an extension (errors, diagnostics); <see langword="null" /> for the built-in folders.</param>
public sealed record ButtonSource(ExtensionDescriptor Extension, bool BuiltIn, bool Shown, ExtensionState? State = null);

/// <summary>One button the user can click, with where it comes from.</summary>
/// <param name="Source">Its folder.</param>
/// <param name="Button">Its declaration in <c>plugin.json</c>.</param>
public sealed record RibbonButtonEntry(ButtonSource Source, ExtensionButton Button);

/// <summary>
///     Every ribbon button, AnalyseTool's own and the extensions' alike: each declared in a <c>plugin.json</c> (<c>ui.buttons</c>),
///     each running a pipeline command (<c>command</c> + <c>args</c>) or opening a page. The ribbon, the clicks and the statistics
///     all read this one list.
/// </summary>
public sealed class ButtonCatalog(ExtensionHost host)
{
    /// <summary>The folder next to the add-in that holds the built-in button folders.</summary>
    public const string RibbonFolder = "Ribbon";

    /// <summary>The id of the buttons of AnalyseTool itself.</summary>
    public const string BuiltInId = "analysetool";

    private readonly Lazy<IReadOnlyList<ExtensionDescriptor>> _builtIn = new(() => LoadBuiltIn(DefaultFolder, IncludeDev));

    /// <summary>Gets the folder of the built-in buttons in the add-in folder.</summary>
    public static string DefaultFolder => Path.Combine(Path.GetDirectoryName(typeof(ButtonCatalog).Assembly.Location) is { Length: > 0 } folder ? folder : AppContext.BaseDirectory, RibbonFolder);

    private static bool IncludeDev =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>The folders with buttons: the built-in ones first, then every extension (shown or not).</summary>
    public IReadOnlyList<ButtonSource> GetSources()
    {
        var sources = _builtIn.Value.Select(descriptor => new ButtonSource(descriptor, true, true)).ToList();
        sources.AddRange(host.GetStates()
            .Where(state => state.Descriptor.HasButtons)
            .Select(state => new ButtonSource(state.Descriptor, false, state.Enabled, state)));
        return sources;
    }

    /// <summary>Finds a clickable button by its key (<see cref="AnalyseTool.Extensions.Usage.ButtonUsageStore.Key" />).</summary>
    public RibbonButtonEntry? Find(string key)
    {
        return GetButtons().FirstOrDefault(entry => string.Equals(AnalyseTool.Extensions.Usage.ButtonUsageStore.Key(entry.Source.Extension.Id, entry.Button.Name), key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every clickable button: the entries of a pulldown instead of the pulldown itself.</summary>
    public IReadOnlyList<RibbonButtonEntry> GetButtons()
    {
        return GetSources().SelectMany(source => Flatten(source.Extension.Manifest.Ui!.EffectiveButtons()).Select(button => new RibbonButtonEntry(source, button))).ToList();
    }

    /// <summary>Reads the built-in button folders: <c>analysetool</c>, and <c>analysetool.dev</c> when asked.</summary>
    /// <exception cref="InvalidDataException">A built-in <c>plugin.json</c> is broken — a bug of the build, not of the user.</exception>
    public static IReadOnlyList<ExtensionDescriptor> LoadBuiltIn(string folder, bool includeDev)
    {
        var result = new List<ExtensionDescriptor>();
        foreach (var name in includeDev ? new[] { BuiltInId, BuiltInId + ".dev" } : [BuiltInId])
        {
            var directory = Path.Combine(folder, name);
            if (!File.Exists(Path.Combine(directory, ExtensionManifest.FileName)))
            {
                continue;
            }

            result.Add(new ExtensionDescriptor(ExtensionManifest.Read(directory), directory, ExtensionZone.Managed, false, ExtensionKind.Page, null, []));
        }

        return result;
    }

    private static IEnumerable<ExtensionButton> Flatten(IEnumerable<ExtensionButton> buttons)
    {
        foreach (var button in buttons)
        {
            if (button.ResolvedKind == ButtonKind.Pulldown)
            {
                foreach (var child in button.Items ?? [])
                {
                    yield return child;
                }
            }
            else
            {
                yield return button;
            }
        }
    }
}
