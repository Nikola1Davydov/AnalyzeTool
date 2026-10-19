using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    /// <summary>
    /// Named sets of parameters for the report ("Fire safety", "Doors handover"…), kept in one JSON file
    /// in the user's AnalyseTool folder — not in the browser's storage, which a cache reset wipes, and not
    /// in the model, because a set is reused across projects. Parameters are identified by GUID (shared)
    /// or name (project parameters), so a set made in one project finds its parameters in the next.
    /// Plain file work, tested without Revit.
    /// </summary>
    internal sealed class ParameterSetStore
    {
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SharedData.ToolData.PLUGIN_NAME, "report-parameter-sets.json");

        private readonly string _path;

        public ParameterSetStore(string? path = null) => _path = path ?? DefaultPath;

        public ParameterSetsResult Load()
        {
            if (!File.Exists(_path)) return new ParameterSetsResult { Path = _path };
            try
            {
                List<ParameterSetDto> sets = JsonConvert.DeserializeObject<List<ParameterSetDto>>(File.ReadAllText(_path)) ?? new();
                return new ParameterSetsResult { Path = _path, Sets = sets.Where(s => s is not null).ToList() };
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return new ParameterSetsResult { Path = _path, Error = ex.Message };
            }
        }

        /// <summary>Writes the whole list. Written to a temp file and swapped in, so a crash mid-write
        /// cannot leave half a file; the previous file stays as .bak.</summary>
        public ParameterSetsResult Save(IEnumerable<ParameterSetDto> sets)
        {
            List<ParameterSetDto> clean = sets
                .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => s with
                {
                    Id = string.IsNullOrWhiteSpace(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                    Name = s.Name.Trim(),
                    Parameters = s.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Guid) || !string.IsNullOrWhiteSpace(p.Name)).ToList(),
                })
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(clean, Formatting.Indented));
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
            else File.Move(temp, _path);
            return new ParameterSetsResult { Path = _path, Sets = clean };
        }
    }

    public sealed record ParameterSetDto
    {
        [JsonProperty("id")]
        [Description("Stable id of the set; empty for a new one.")]
        public string Id { get; init; } = string.Empty;

        [JsonProperty("name")]
        [Description("Name the user gave the set.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("parameters")]
        [Description("The parameters, in report order.")]
        public List<ParameterRefDto> Parameters { get; init; } = new();

        [JsonProperty("updated")]
        [Description("When the set was last saved (UTC, ISO 8601).")]
        public string Updated { get; init; } = string.Empty;

        /// <summary>The Revit project the set belongs to (the document's CreationGUID, which survives a
        /// rename or a move), or null for a set offered in every project.</summary>
        [JsonProperty("projectId", NullValueHandling = NullValueHandling.Ignore)]
        [Description("CreationGUID of the Revit project the set belongs to; absent = available in all projects.")]
        public string? ProjectId { get; init; }

        [JsonProperty("projectName", NullValueHandling = NullValueHandling.Ignore)]
        [Description("Title of that project when the set was saved — for display only.")]
        public string? ProjectName { get; init; }
    }

    public sealed record ParameterRefDto
    {
        [JsonProperty("guid", NullValueHandling = NullValueHandling.Ignore)]
        [Description("GUID of a shared parameter.")]
        public string? Guid { get; init; }

        [JsonProperty("name")]
        [Description("Parameter name — identifies a project parameter, labels a shared one.")]
        public string Name { get; init; } = string.Empty;
    }

    public sealed record ParameterSetsResult
    {
        [JsonProperty("path")]
        [Description("The file the sets live in.")]
        public string Path { get; init; } = string.Empty;

        [JsonProperty("sets")]
        [Description("All saved sets.")]
        public List<ParameterSetDto> Sets { get; init; } = new();

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        [Description("Why the file could not be read, if it could not.")]
        public string? Error { get; init; }
    }
}
