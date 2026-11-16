using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalyseTool.Services.SharedParameters;

/// <summary>A named set of parameters for the report.</summary>
public sealed record ParameterSetDto
{
    /// <summary>The stable id; empty for a new set.</summary>
    [Description("Stable id of the set; empty for a new one.")]
    public string Id { get; init; } = string.Empty;

    /// <summary>The name the user gave the set.</summary>
    [Description("Name the user gave the set.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The parameters, in report order.</summary>
    [Description("The parameters, in report order.")]
    public IReadOnlyList<ParameterRefDto> Parameters { get; init; } = [];

    /// <summary>When the set was last saved (UTC, ISO 8601).</summary>
    [Description("When the set was last saved (UTC, ISO 8601).")]
    public string Updated { get; init; } = string.Empty;

    /// <summary>The <c>CreationGUID</c> of the project the set belongs to; null for a set offered in every project.</summary>
    [Description("CreationGUID of the Revit project the set belongs to; absent = available in all projects.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; init; }

    /// <summary>The title of that project when the set was saved — for display only.</summary>
    [Description("Title of that project when the set was saved — for display only.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectName { get; init; }
}

/// <summary>A parameter of a set: by GUID (shared) or by name (project parameter).</summary>
public sealed record ParameterRefDto
{
    /// <summary>The GUID of a shared parameter.</summary>
    [Description("GUID of a shared parameter.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Guid { get; init; }

    /// <summary>The name — identifies a project parameter, labels a shared one.</summary>
    [Description("Parameter name — identifies a project parameter, labels a shared one.")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>The saved sets.</summary>
public sealed record ParameterSetsResult
{
    /// <summary>The file the sets live in.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>All saved sets.</summary>
    public IReadOnlyList<ParameterSetDto> Sets { get; init; } = [];

    /// <summary>Why the file could not be read, if it could not.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>
///     Named sets of parameters for the report, kept in one JSON file in the user's AnalyseTool folder — not in the
///     browser's storage, which a cache reset wipes, and not in the model, because a set is reused across projects. Plain
///     file work, tested without Revit.
/// </summary>
public sealed class ParameterSetStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    /// <summary>Creates a store over <paramref name="path" />, or over <see cref="DefaultPath" />.</summary>
    public ParameterSetStore(string? path = null)
    {
        _path = path ?? DefaultPath;
    }

    /// <summary>The file of 1.x: <c>%LOCALAPPDATA%\AnalyseTool\report-parameter-sets.json</c>, shared by every Revit version.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AnalyseTool",
        "report-parameter-sets.json");

    /// <summary>The saved sets; none when the file does not exist, an error when it cannot be read.</summary>
    public ParameterSetsResult Load()
    {
        if (!File.Exists(_path))
        {
            return new ParameterSetsResult { Path = _path };
        }

        try
        {
            var sets = JsonSerializer.Deserialize<List<ParameterSetDto?>>(File.ReadAllText(_path), SerializerOptions) ?? [];
            return new ParameterSetsResult { Path = _path, Sets = sets.OfType<ParameterSetDto>().ToList() };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ParameterSetsResult { Path = _path, Error = exception.Message };
        }
    }

    /// <summary>
    ///     Replaces the whole list. Written to a temporary file and swapped in, so a crash mid-write cannot leave half a file;
    ///     the previous file stays as <c>.bak</c>. A set without a name is dropped, a new one gets an id.
    /// </summary>
    public ParameterSetsResult Save(IEnumerable<ParameterSetDto?> sets)
    {
        var clean = sets
            .OfType<ParameterSetDto>()
            .Where(set => !string.IsNullOrWhiteSpace(set.Name))
            .Select(set => set with
            {
                Id = string.IsNullOrWhiteSpace(set.Id) ? Guid.NewGuid().ToString("N") : set.Id,
                Name = set.Name.Trim(),
                Parameters = (set.Parameters ?? [])
                    .Where(parameter => parameter is not null && (!string.IsNullOrWhiteSpace(parameter.Guid) || !string.IsNullOrWhiteSpace(parameter.Name)))
                    .ToList()
            })
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(clean, SerializerOptions));
        if (File.Exists(_path))
        {
            File.Replace(temp, _path, _path + ".bak");
        }
        else
        {
            File.Move(temp, _path);
        }

        return new ParameterSetsResult { Path = _path, Sets = clean };
    }
}
