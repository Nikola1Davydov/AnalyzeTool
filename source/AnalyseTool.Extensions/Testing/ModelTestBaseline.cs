using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Extensions.Testing;

/// <summary>An accepted failure.</summary>
/// <param name="Key">The case key.</param>
/// <param name="Message">What failed when it was accepted.</param>
/// <param name="By">Who accepted it.</param>
/// <param name="At">When.</param>
public sealed record BaselineEntry(string Key, string Message, string By, DateTimeOffset At);

/// <summary>The accepted failures of one suite on one model: <c>baseline\&lt;model&gt;.json</c>.</summary>
public sealed class BaselineFile
{
    /// <summary>Gets or sets the title of the model, for people reading the file.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets the accepted failures by <c>&lt;suite class&gt;/&lt;test id&gt;</c> — suites of one extension share the file.</summary>
    public SortedDictionary<string, List<BaselineEntry>> Tests { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
///     Accepted failures: an existing model starts with hundreds, and only new ones should fail. They live with the suite —
///     in the extension folder, versioned and shared with it — or in the profile for a suite from an installed package.
/// </summary>
internal static class ModelTestBaseline
{
    public const string FolderName = "baseline";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string PathOf(string baselineDirectory, string modelKey)
    {
        var safe = string.Concat(modelKey.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(baselineDirectory, FolderName, safe + ".json");
    }

    public static BaselineFile Load(string baselineDirectory, string modelKey)
    {
        var path = PathOf(baselineDirectory, modelKey);
        if (!File.Exists(path))
        {
            return new BaselineFile();
        }

        try
        {
            var file = JsonSerializer.Deserialize<BaselineFile>(File.ReadAllText(path), Json) ?? new BaselineFile();
            file.Tests = new SortedDictionary<string, List<BaselineEntry>>(file.Tests, StringComparer.OrdinalIgnoreCase);
            return file;
        }
        catch (JsonException)
        {
            // A broken file accepts nothing: every failure shows, nothing is silently hidden
            return new BaselineFile();
        }
    }

    public static IReadOnlySet<string> Accepted(BaselineFile file, string localTestId)
    {
        return file.Tests.TryGetValue(localTestId, out var entries) ? entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
    }

    /// <summary>Accepts or removes cases of a test and writes the file; an empty file is deleted.</summary>
    /// <returns>How many entries the test has now.</returns>
    public static int Change(string baselineDirectory, string modelKey, string modelTitle, string localTestId, IEnumerable<ModelTestCase> accept, IEnumerable<string> remove, string by)
    {
        var file = Load(baselineDirectory, modelKey);
        file.Model = modelTitle;
        var entries = file.Tests.TryGetValue(localTestId, out var existing) ? existing : [];
        var removed = remove.ToHashSet(StringComparer.Ordinal);
        entries.RemoveAll(entry => removed.Contains(entry.Key));
        var known = entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in accept.Where(item => known.Add(item.Key)))
        {
            entries.Add(new BaselineEntry(item.Key, item.Message, by, DateTimeOffset.Now));
        }

        if (entries.Count == 0)
        {
            file.Tests.Remove(localTestId);
        }
        else
        {
            file.Tests[localTestId] = entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();
        }

        var path = PathOf(baselineDirectory, modelKey);
        if (file.Tests.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, Json));
        }

        return entries.Count;
    }
}
