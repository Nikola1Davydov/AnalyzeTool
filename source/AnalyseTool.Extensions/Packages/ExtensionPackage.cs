using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Extensions.Packages;

/// <summary>A checked extension package.</summary>
/// <param name="Manifest">Its <c>plugin.json</c>.</param>
/// <param name="Prefix">The folder inside the zip that holds the extension, with a trailing <c>/</c>; empty for the root.</param>
/// <param name="BinaryYears">The Revit years it ships DLLs for.</param>
public sealed record ExtensionPackageInfo(ExtensionManifest Manifest, string Prefix, IReadOnlyList<string> BinaryYears);

/// <summary>
///     A zip with one extension folder — the format of 1.x: <c>plugin.json</c> at the root or in one top-level folder, DLLs
///     in <c>&lt;year&gt;\</c>, sources in <c>src\</c>.
/// </summary>
public static class ExtensionPackage
{
    private static readonly Regex YearFolder = new(@"^\d{4}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Checks a package: one manifest with an id and a version, no path that leaves the folder, the declared DLL present.</summary>
    /// <exception cref="CommandException"><see cref="ExtensionErrorCodes.Invalid" /> with the reason.</exception>
    public static ExtensionPackageInfo Validate(string zipPath)
    {
        using var zip = Open(zipPath);
        var entries = zip.Entries.ToDictionary(entry => entry.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        foreach (var name in entries.Keys)
        {
            if (name.Split('/').Any(segment => segment == "..") || Path.IsPathRooted(name))
            {
                throw Invalid($"The package contains an unsafe path: {name}");
            }
        }

        var prefix = LocateManifest(entries.Keys);
        ExtensionManifest? manifest;
        using (var reader = new StreamReader(entries[prefix + ExtensionManifest.FileName].Open()))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<ExtensionManifest>(reader.ReadToEnd(), ReadOptions);
            }
            catch (JsonException exception)
            {
                throw Invalid($"plugin.json is not valid JSON: {exception.Message}");
            }
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw Invalid("plugin.json has no 'id'.");
        }

        if (!IsValidId(manifest.Id))
        {
            throw Invalid($"Invalid extension id '{manifest.Id}': only letters, digits, '.', '-' and '_' are allowed.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw Invalid("plugin.json has no 'version'.");
        }

        var years = entries.Keys
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[prefix.Length..])
            .Where(name => name.Contains('/'))
            .Select(name => name.Split('/')[0])
            .Where(segment => YearFolder.IsMatch(segment))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrWhiteSpace(manifest.EntryAssembly) && !string.Equals(manifest.Build, "host", StringComparison.OrdinalIgnoreCase))
        {
            var entry = manifest.EntryAssembly.Replace('\\', '/');
            var hasSources = entries.Keys.Any(name => name.StartsWith(prefix + "src/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            if (!hasSources && !entries.ContainsKey(prefix + entry) && !years.Any(year => entries.ContainsKey($"{prefix}{year}/{entry}")))
            {
                throw Invalid($"entryAssembly '{manifest.EntryAssembly}' is declared but the package has no such file — expected it in a Revit-year folder (e.g. '2025/').");
            }
        }

        return new ExtensionPackageInfo(manifest, prefix, years);
    }

    /// <summary>Extracts the extension folder of a checked package into <paramref name="target" />.</summary>
    public static void ExtractTo(string zipPath, ExtensionPackageInfo info, string target)
    {
        using var zip = Open(zipPath);
        var fullTarget = Path.GetFullPath(target);
        Directory.CreateDirectory(fullTarget);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(info.Prefix, StringComparison.OrdinalIgnoreCase) || name.Length == info.Prefix.Length)
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(fullTarget, name[info.Prefix.Length..].Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid($"A package entry leaves the target folder: {entry.FullName}");
            }

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>Whether an id is safe as a folder name and as a command prefix.</summary>
    public static bool IsValidId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains("..", StringComparison.Ordinal) || !char.IsLetterOrDigit(id[0]) || !char.IsLetterOrDigit(id[^1]))
        {
            return false;
        }

        return id.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
               && !id.EndsWith(".installing", StringComparison.OrdinalIgnoreCase)
               && !id.EndsWith(".old", StringComparison.OrdinalIgnoreCase)
               && !ReservedNames.Contains(id.Split('.')[0]);
    }

    private static ZipArchive Open(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            throw new CommandException(CommandErrorCodes.NotFound, $"There is no package {zipPath}.");
        }

        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw Invalid($"Not a readable zip package: {exception.Message}");
        }
    }

    private static string LocateManifest(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Any(name => string.Equals(name, ExtensionManifest.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            return "";
        }

        var candidates = list
            .Where(name => name.Count(character => character == '/') == 1 && name.EndsWith("/" + ExtensionManifest.FileName, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[..(name.IndexOf('/') + 1)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw Invalid("No plugin.json — expected it at the zip root or inside one top-level folder."),
            _ => throw Invalid("The zip has several top-level folders with a plugin.json — a package holds exactly one extension.")
        };
    }

    private static CommandException Invalid(string message)
    {
        return new CommandException(ExtensionErrorCodes.Invalid, message);
    }
}
