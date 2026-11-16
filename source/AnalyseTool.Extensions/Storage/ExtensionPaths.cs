using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnalyseTool.Extensions.Storage;

/// <summary>Where an extension folder comes from: the user's own folders, or packages the manager installed.</summary>
public enum ExtensionZone
{
    /// <summary>Installed by the extension manager (<c>extensions-dist</c>): updated and removed by it.</summary>
    Managed,

    /// <summary>The user's own folders (<c>extensions</c> and every added folder): created, edited and versioned by the user.</summary>
    Dev
}

/// <summary>A folder that holds extension folders.</summary>
/// <param name="Path">The full path.</param>
/// <param name="Zone">Who owns its extensions.</param>
/// <param name="IsDefault">One of the two built-in roots, which cannot be removed.</param>
public sealed record ExtensionRoot(string Path, ExtensionZone Zone, bool IsDefault);

/// <summary>
///     The folders of the extensions and the files of their state — the same places as AnalyseTool 1.x, so the extensions
///     of 1.x load unchanged.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item><c>%LOCALAPPDATA%\AnalyseTool\extensions\</c> — the user's own extensions: where «New button» creates one.</item>
///         <item><c>%LOCALAPPDATA%\AnalyseTool\extensions-dist\</c> — installed packages.</item>
///         <item><c>extensions.json</c> — further folders the user added (a network share, a git checkout) and the authoring root.</item>
///         <item><c>extensions-state.json</c> — disabled extensions and the versions extensions are pinned to.</item>
///     </list>
/// </remarks>
public sealed class ExtensionPaths(string profileRoot)
{
    /// <summary>Gets the profile folder: <c>%LOCALAPPDATA%\AnalyseTool</c>, a temp folder in tests.</summary>
    public string ProfileRoot { get; } = profileRoot;

    /// <summary>Gets the default folder of the user's own extensions.</summary>
    public string DevRoot => Path.Combine(ProfileRoot, "extensions");

    /// <summary>Gets the folder of installed packages.</summary>
    public string ManagedRoot => Path.Combine(ProfileRoot, "extensions-dist");

    /// <summary>Gets the file with the user's added folders.</summary>
    public string SourcesFile => Path.Combine(ProfileRoot, "extensions.json");

    /// <summary>Gets the file with the state of extensions.</summary>
    public string StateFile => Path.Combine(ProfileRoot, "extensions-state.json");

    /// <summary>Returns the paths of the profile of the current user.</summary>
    public static ExtensionPaths ForCurrentUser()
    {
        return new ExtensionPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalyseTool"));
    }

    /// <summary>Gets every root: the two built-in ones first, then the user's.</summary>
    public IReadOnlyList<ExtensionRoot> GetRoots()
    {
        var roots = new List<ExtensionRoot>
        {
            new(ManagedRoot, ExtensionZone.Managed, true),
            new(DevRoot, ExtensionZone.Dev, true)
        };

        foreach (var path in ReadSources().Paths)
        {
            if (!roots.Any(root => SamePath(root.Path, path)))
            {
                roots.Add(new ExtensionRoot(path, ExtensionZone.Dev, false));
            }
        }

        return roots;
    }

    /// <summary>Gets the folder where new extensions are created: the user's choice among their roots, otherwise <see cref="DevRoot" />.</summary>
    public string AuthoringRoot
    {
        get
        {
            var stored = ReadSources().AuthoringRoot;
            return !string.IsNullOrWhiteSpace(stored) && GetRoots().Any(root => root.Zone == ExtensionZone.Dev && SamePath(root.Path, stored))
                ? stored
                : DevRoot;
        }
    }

    /// <summary>Adds a folder of extensions; returns its full path.</summary>
    public string AddRoot(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        if (SamePath(full, DevRoot) || SamePath(full, ManagedRoot))
        {
            return full;
        }

        var sources = ReadSources();
        if (!sources.Paths.Any(existing => SamePath(existing, full)))
        {
            sources.Paths.Add(full);
            WriteSources(sources);
        }

        return full;
    }

    /// <summary>Removes a folder the user added; its files stay on disk.</summary>
    public void RemoveRoot(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var sources = ReadSources();
        if (sources.Paths.RemoveAll(existing => SamePath(existing, full)) > 0)
        {
            if (sources.AuthoringRoot is not null && SamePath(sources.AuthoringRoot, full))
            {
                sources.AuthoringRoot = null;
            }

            WriteSources(sources);
        }
    }

    /// <summary>Makes one of the user's roots the place where new extensions are created.</summary>
    /// <returns>Why it could not, or <see langword="null" />.</returns>
    public string? SetAuthoringRoot(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var root = GetRoots().FirstOrDefault(candidate => SamePath(candidate.Path, full));
        if (root is null)
        {
            return $"'{full}' is not a folder of extensions. Add it first.";
        }

        if (root.Zone != ExtensionZone.Dev)
        {
            return "Installed packages are managed by the extension manager: an update would overwrite anything created there.";
        }

        var sources = ReadSources();
        sources.AuthoringRoot = full;
        WriteSources(sources);
        return null;
    }

    /// <summary>Compares two paths as Windows does.</summary>
    public static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private Sources ReadSources()
    {
        var node = JsonFile.Read(SourcesFile);
        var sources = new Sources
        {
            AuthoringRoot = node?["AuthoringRoot"]?.GetValue<string>() ?? node?["authoringRoot"]?.GetValue<string>()
        };

        if ((node?["Paths"] ?? node?["paths"]) is JsonArray paths)
        {
            sources.Paths.AddRange(paths.Select(path => path?.GetValue<string>()).OfType<string>().Where(path => path.Length > 0));
        }

        return sources;
    }

    private void WriteSources(Sources sources)
    {
        // The names of 1.x (Newtonsoft, PascalCase): a 1.x install reading the same profile keeps working
        JsonFile.Write(SourcesFile, new JsonObject
        {
            ["Paths"] = new JsonArray(sources.Paths.Select(path => (JsonNode)path).ToArray()),
            ["AuthoringRoot"] = sources.AuthoringRoot
        });
    }

    private sealed class Sources
    {
        public List<string> Paths { get; } = [];

        public string? AuthoringRoot { get; set; }
    }
}

/// <summary>Small JSON files of the profile: read tolerant, written atomically.</summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Reads the file; <see langword="null" /> when it is missing or not JSON.</summary>
    public static JsonNode? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the file through a temp file, so a crash never leaves half a file.</summary>
    public static void Write(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, node.ToJsonString(Indented));
        File.Move(temp, path, overwrite: true);
    }
}
