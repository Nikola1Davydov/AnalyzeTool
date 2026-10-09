namespace AnalyseTool.Tools.SharedParameters
{
    /// <summary>
    /// The editor's two moves on the shared parameter file: read it into the wire shape, and write the
    /// wire shape back. Plain file work, no Revit — which file is "Revit's current one" is the command's
    /// business, so this stays testable without Revit.
    /// </summary>
    internal static class SharedParameterFileService
    {
        public static SharedParameterFileData Read(string? path, string? revitCurrent)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new SharedParameterFileData { Path = null, Exists = false };

            bool isCurrent = SamePath(path, revitCurrent);
            if (!File.Exists(path))
                return new SharedParameterFileData { Path = path, Exists = false, IsRevitCurrent = isCurrent };

            try
            {
                SharedParameterFile file = SharedParameterFile.Load(path);
                return ToData(file, path, isCurrent);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                return new SharedParameterFileData
                {
                    Path = path,
                    Exists = true,
                    IsRevitCurrent = isCurrent,
                    Stamp = SharedParameterFile.Stamp(path),
                    Error = ex.Message,
                };
            }
        }

        /// <summary>
        /// Writes the editor's copy. What the editor does not hold — header comments, META, unknown
        /// columns and sections, the encoding — is taken from the file on disk, so a save never drops
        /// what the editor cannot show. Refuses when the file changed since the editor read it.
        /// </summary>
        public static SharedParameterFileData Save(string path, string? expectedStamp, IEnumerable<SharedParameterGroupDto> groups,
            IEnumerable<SharedParameterDto> parameters, string? revitCurrent)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("No file path given.");

            bool exists = File.Exists(path);
            if (exists && expectedStamp is not null && SharedParameterFile.Stamp(path) != expectedStamp)
                throw new InvalidOperationException(
                    "The file was changed outside the editor since it was opened. Reload it, then make the change again.");

            SharedParameterFile file = exists ? SharedParameterFile.Load(path) : SharedParameterFile.CreateEmpty();
            Dictionary<Guid, SharedParameterDefinition> before = file.Parameters
                .GroupBy(p => p.Guid).ToDictionary(g => g.Key, g => g.First());

            file.Groups.Clear();
            foreach (SharedParameterGroupDto g in groups)
                file.Groups.Add(new SharedParameterGroup { Id = g.Id, Name = g.Name.Trim() });

            file.Parameters.Clear();
            foreach (SharedParameterDto dto in parameters)
            {
                Guid guid = Guid.TryParse(dto.Guid, out Guid parsed) ? parsed : Guid.Empty;
                SharedParameterDefinition p = new()
                {
                    Guid = guid,
                    Name = dto.Name.Trim(),
                    DataType = dto.DataType.Trim(),
                    DataCategory = dto.DataCategory.Trim(),
                    GroupId = dto.GroupId,
                    Visible = dto.Visible,
                    Description = dto.Description,
                    UserModifiable = dto.UserModifiable,
                    HideWhenNoValue = dto.HideWhenNoValue,
                };
                if (before.TryGetValue(guid, out SharedParameterDefinition? old))
                    foreach ((string column, string value) in old.ExtraColumns) p.ExtraColumns[column] = value;
                file.Parameters.Add(p);
            }

            List<string> problems = file.Validate();
            if (problems.Count > 0)
                throw new InvalidOperationException("Not saved:\n" + string.Join("\n", problems));

            file.Save(path);
            return ToData(file, path, SamePath(path, revitCurrent));
        }

        private static SharedParameterFileData ToData(SharedParameterFile file, string path, bool isCurrent) => new()
        {
            Path = path,
            Exists = true,
            IsRevitCurrent = isCurrent,
            Stamp = SharedParameterFile.Stamp(path),
            Groups = file.Groups.Select(g => new SharedParameterGroupDto { Id = g.Id, Name = g.Name }).ToList(),
            Parameters = file.Parameters.Select(p => new SharedParameterDto
            {
                Guid = p.Guid.ToString("D"),
                Name = p.Name,
                DataType = p.DataType,
                DataCategory = p.DataCategory,
                GroupId = p.GroupId,
                Visible = p.Visible,
                Description = p.Description,
                UserModifiable = p.UserModifiable,
                HideWhenNoValue = p.HideWhenNoValue,
            }).ToList(),
        };

        public static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }
}
