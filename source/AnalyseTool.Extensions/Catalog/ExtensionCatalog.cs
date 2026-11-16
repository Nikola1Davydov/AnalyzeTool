using System.IO;
using System.Text.RegularExpressions;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Extensions.Catalog;

/// <summary>How the commands of an extension come to be.</summary>
public enum ExtensionKind
{
    /// <summary>C# sources in <c>src\</c> that AnalyseTool compiles — the default for a new extension.</summary>
    Source,

    /// <summary>A DLL the author builds, one per Revit year.</summary>
    Dll,

    /// <summary>No commands: a page only.</summary>
    Page
}

/// <summary>An extension folder as found on disk.</summary>
/// <param name="Manifest">Its <c>plugin.json</c>.</param>
/// <param name="Directory">The folder.</param>
/// <param name="Zone">Who owns it.</param>
/// <param name="IsLegacyLayout">It sits in the 1.x layout <c>&lt;root&gt;\&lt;year&gt;\&lt;id&gt;</c>.</param>
/// <param name="Kind">How its commands come to be.</param>
/// <param name="EntryAssemblyPath">For <see cref="ExtensionKind.Dll" />: the DLL for the running Revit year; <see langword="null" /> when there is none.</param>
/// <param name="BinaryYears">The Revit years a DLL extension ships builds for.</param>
public sealed record ExtensionDescriptor(
    ExtensionManifest Manifest,
    string Directory,
    ExtensionZone Zone,
    bool IsLegacyLayout,
    ExtensionKind Kind,
    string? EntryAssemblyPath,
    IReadOnlyList<string> BinaryYears)
{
    /// <summary>Gets the id.</summary>
    public string Id => Manifest.Id;

    /// <summary>Gets a value indicating whether the extension has ribbon buttons.</summary>
    public bool HasButtons => Manifest.Ui?.EffectiveButtons().Count > 0;

    /// <summary>Gets a value indicating whether a button opens the page — the page is then the way in, and its commands its backend.</summary>
    public bool OpensPage => Manifest.Ui?.EffectiveButtons().Any(button => string.IsNullOrWhiteSpace(button.Command)) == true;

    /// <summary>The tab a button folder belongs to; <see langword="null" /> for an extension of plugin.json (1.x).</summary>
    public ExtensionTab? Tab { get; init; }

    /// <summary>Gets a value indicating whether this Revit can load its commands: a DLL extension needs a build for this year.</summary>
    public bool IsCompatible => Kind != ExtensionKind.Dll || EntryAssemblyPath is not null;
}

/// <summary>
///     Finds the extension folders: <c>&lt;root&gt;\&lt;id&gt;\plugin.json</c>, and the 1.x layout
///     <c>&lt;root&gt;\&lt;year&gt;\&lt;id&gt;\plugin.json</c> for the running year only.
/// </summary>
public sealed class ExtensionCatalog(ExtensionPaths paths, string revitVersion)
{
    /// <summary>The folder of the C# sources the host compiles.</summary>
    public const string SourceFolder = "src";

    private static readonly Regex YearFolder = new(@"^\d{4}$", RegexOptions.Compiled);

    /// <summary>Gets the Revit year: <c>2025</c>, <c>2026</c>, <c>2027</c>.</summary>
    public string RevitVersion { get; } = revitVersion;

    /// <summary>Scans every root.</summary>
    /// <param name="problems">Receives the folders that hold a <c>plugin.json</c> that cannot be read: folder → reason.</param>
    public IReadOnlyList<ExtensionDescriptor> Scan(IDictionary<string, string>? problems = null)
    {
        return paths.GetRoots().SelectMany(root => ScanRoot(root, problems)).ToList();
    }

    /// <summary>The tab folders of the user's roots (not of packages), those without a button yet too.</summary>
    public IReadOnlyList<ExtensionTab> GetTabs()
    {
        var result = new List<ExtensionTab>();
        foreach (var root in paths.GetRoots().Where(root => root.Zone == ExtensionZone.Dev && Directory.Exists(root.Path)))
        {
            foreach (var directory in Directory.GetDirectories(root.Path).Where(ButtonFolder.IsTab))
            {
                try
                {
                    result.Add(ButtonFolder.ReadTab(directory));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                }
            }
        }

        return result;
    }

    /// <summary>Finds a tab of the user by its id (<c>sheets.tab</c>).</summary>
    public ExtensionTab? FindTab(string id)
    {
        return GetTabs().FirstOrDefault(tab => string.Equals(tab.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds an extension by id.</summary>
    public ExtensionDescriptor? Find(string id)
    {
        return Scan().FirstOrDefault(descriptor => string.Equals(descriptor.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Scans one root.</summary>
    public IReadOnlyList<ExtensionDescriptor> ScanRoot(ExtensionRoot root, IDictionary<string, string>? problems = null)
    {
        var result = new List<ExtensionDescriptor>();
        if (!Directory.Exists(root.Path))
        {
            return result;
        }

        foreach (var directory in Directory.GetDirectories(root.Path))
        {
            var name = Path.GetFileName(directory);
            // The work folders of an install, even when a crash left them with a plugin.json inside
            if (name.EndsWith(".installing", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".old", StringComparison.OrdinalIgnoreCase) || name.StartsWith('.'))
            {
                continue;
            }

            // A tab: every button folder in it is an extension of its own
            if (name.EndsWith(ButtonFolder.TabSuffix, StringComparison.OrdinalIgnoreCase))
            {
                ExtensionTab tab;
                try
                {
                    tab = ButtonFolder.ReadTab(directory);
                }
                catch (InvalidDataException exception)
                {
                    problems?.TryAdd(directory, exception.Message);
                    continue;
                }

                foreach (var button in tab.ButtonDirectories)
                {
                    Add(result, button, root.Zone, false, problems, tab);
                }

                continue;
            }

            if (YearFolder.IsMatch(name))
            {
                if (name == RevitVersion)
                {
                    foreach (var legacy in Directory.GetDirectories(directory))
                    {
                        Add(result, legacy, root.Zone, true, problems);
                    }
                }

                continue;
            }

            Add(result, directory, root.Zone, false, problems);
        }

        return result;
    }

    /// <summary>Reads one extension folder; <see langword="null" /> when it is not one.</summary>
    public ExtensionDescriptor? Read(string directory, ExtensionZone zone = ExtensionZone.Dev, bool isLegacyLayout = false)
    {
        var list = new List<ExtensionDescriptor>();
        Add(list, directory, zone, isLegacyLayout, null);
        return list.FirstOrDefault();
    }

    /// <summary>
    ///     The C# sources of a folder, in a stable order: every <c>*.cs</c> of <c>src\</c>; for a button folder, its own <c>*.cs</c>
    ///     and the <c>_shared\</c> of its tab.
    /// </summary>
    public static IReadOnlyList<string> GetSourceFiles(string directory)
    {
        if (ButtonFolder.IsButton(directory))
        {
            return ButtonFolder.SourceFiles(directory);
        }

        var source = Path.Combine(directory, SourceFolder);
        return Directory.Exists(source)
            ? Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(Path.GetRelativePath(source, file)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
    }

    /// <summary>Decides how the commands of a folder come to be.</summary>
    public static ExtensionKind GetKind(ExtensionManifest manifest, string directory)
    {
        var build = manifest.Build?.Trim().ToLowerInvariant();
        var hasSources = GetSourceFiles(directory).Count > 0;
        if (build == "host")
        {
            return ExtensionKind.Source;
        }

        if (build == "author")
        {
            return string.IsNullOrWhiteSpace(manifest.EntryAssembly) ? ExtensionKind.Page : ExtensionKind.Dll;
        }

        // As in 1.x: sources and no project file — the host builds; a project file means the author does
        if (hasSources && !Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).Any())
        {
            return ExtensionKind.Source;
        }

        return string.IsNullOrWhiteSpace(manifest.EntryAssembly) ? ExtensionKind.Page : ExtensionKind.Dll;
    }

    private void Add(List<ExtensionDescriptor> result, string directory, ExtensionZone zone, bool isLegacy, IDictionary<string, string>? problems, ExtensionTab? tab = null)
    {
        if (!ExtensionManifest.Exists(directory))
        {
            return;
        }

        try
        {
            var manifest = ExtensionManifest.Read(directory);
            var kind = GetKind(manifest, directory);
            result.Add(new ExtensionDescriptor(
                manifest,
                directory,
                zone,
                isLegacy,
                kind,
                kind == ExtensionKind.Dll ? ResolveEntryAssembly(manifest, directory, isLegacy) : null,
                isLegacy ? [] : GetBinaryYears(directory))
            {
                Tab = tab ?? (ButtonFolder.IsButton(directory) && ButtonFolder.TabDirectory(directory) is { } parent ? ButtonFolder.ReadTab(parent) : null)
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problems?.TryAdd(directory, exception.Message);
        }
    }

    private string? ResolveEntryAssembly(ExtensionManifest manifest, string directory, bool isLegacy)
    {
        if (!isLegacy)
        {
            var versioned = Path.Combine(directory, RevitVersion, manifest.EntryAssembly!);
            if (File.Exists(versioned))
            {
                return versioned;
            }
        }

        var plain = Path.Combine(directory, manifest.EntryAssembly!);
        return File.Exists(plain) ? plain : null;
    }

    private static IReadOnlyList<string> GetBinaryYears(string directory)
    {
        return Directory.GetDirectories(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => YearFolder.IsMatch(name))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildOutput(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase) || first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }
}
