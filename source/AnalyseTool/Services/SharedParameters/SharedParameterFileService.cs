using System.IO;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Services.SharedParameters;

/// <summary>
///     The error codes of the shared parameter file commands, next to <see cref="CommandErrorCodes" />.
/// </summary>
public static class SharedParameterErrorCodes
{
    /// <summary>The file changed on disk since the caller read it.</summary>
    public const string FileChanged = "file_changed";

    /// <summary>The file would be invalid: names, GUIDs or group ids collide, a group is missing.</summary>
    public const string InvalidFile = "invalid_file";

    /// <summary>Revit has no shared parameter file set, or it cannot be read.</summary>
    public const string NoSharedParameterFile = "no_shared_parameter_file";
}

/// <summary>
///     The editor's two moves on the shared parameter file: read it into the wire shape, and write the wire shape back.
///     Plain file work, no Revit — which file is Revit's current one is the command's business.
/// </summary>
public static class SharedParameterFileService
{
    /// <summary>Reads the file at <paramref name="path" />; a broken file is reported in <see cref="SharedParameterFileData.Error" />.</summary>
    public static SharedParameterFileData Read(string? path, string? revitCurrent)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new SharedParameterFileData { Path = null, Exists = false };
        }

        var isCurrent = SamePath(path, revitCurrent);
        if (!File.Exists(path))
        {
            return new SharedParameterFileData { Path = path, Exists = false, IsRevitCurrent = isCurrent };
        }

        try
        {
            return ToData(SharedParameterFile.Load(path), path, isCurrent);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            return new SharedParameterFileData
            {
                Path = path,
                Exists = true,
                IsRevitCurrent = isCurrent,
                Stamp = SharedParameterFile.Stamp(path),
                Error = exception.Message
            };
        }
    }

    /// <summary>
    ///     Writes the editor's copy. What the editor does not hold — header comments, META, unknown columns and sections, the
    ///     encoding — is taken from the file on disk, so a save never drops what the editor cannot show.
    /// </summary>
    /// <exception cref="CommandException">
    ///     <see cref="SharedParameterErrorCodes.FileChanged" /> when the file changed since <paramref name="expectedStamp" />;
    ///     <see cref="SharedParameterErrorCodes.InvalidFile" /> when the result would be invalid; <c>bad_request</c> without a path.
    /// </exception>
    public static SharedParameterFileData Save(
        string path,
        string? expectedStamp,
        IEnumerable<SharedParameterGroupDto> groups,
        IEnumerable<SharedParameterDto> parameters,
        string? revitCurrent)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "No file path given.");
        }

        var exists = File.Exists(path);
        if (exists && expectedStamp is not null && SharedParameterFile.Stamp(path) != expectedStamp)
        {
            throw new CommandException(SharedParameterErrorCodes.FileChanged,
                "The file was changed outside the editor since it was opened. Reload it, then make the change again.");
        }

        var file = exists ? SharedParameterFile.Load(path) : SharedParameterFile.CreateEmpty();
        var before = file.Parameters.GroupBy(parameter => parameter.Guid).ToDictionary(group => group.Key, group => group.First());

        file.Groups.Clear();
        foreach (var group in groups)
        {
            file.Groups.Add(new SharedParameterGroup { Id = group.Id, Name = (group.Name ?? string.Empty).Trim() });
        }

        file.Parameters.Clear();
        foreach (var dto in parameters)
        {
            var guid = Guid.TryParse(dto.Guid, out var parsed) ? parsed : Guid.Empty;
            var parameter = new SharedParameterDefinition
            {
                Guid = guid,
                Name = (dto.Name ?? string.Empty).Trim(),
                DataType = (dto.DataType ?? string.Empty).Trim(),
                DataCategory = (dto.DataCategory ?? string.Empty).Trim(),
                GroupId = dto.GroupId,
                Visible = dto.Visible,
                Description = dto.Description ?? string.Empty,
                UserModifiable = dto.UserModifiable,
                HideWhenNoValue = dto.HideWhenNoValue
            };

            if (before.TryGetValue(guid, out var old))
            {
                foreach (var (column, value) in old.ExtraColumns)
                {
                    parameter.ExtraColumns[column] = value;
                }
            }

            file.Parameters.Add(parameter);
        }

        var problems = file.Validate();
        if (problems.Count > 0)
        {
            throw new CommandException(SharedParameterErrorCodes.InvalidFile, "Not saved:\n" + string.Join("\n", problems));
        }

        file.Save(path);
        return ToData(file, path, SamePath(path, revitCurrent));
    }

    /// <summary>Whether two paths name the same file; false when either is empty or invalid.</summary>
    public static bool SamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static SharedParameterFileData ToData(SharedParameterFile file, string path, bool isCurrent)
    {
        return new SharedParameterFileData
        {
            Path = path,
            Exists = true,
            IsRevitCurrent = isCurrent,
            Stamp = SharedParameterFile.Stamp(path),
            Groups = file.Groups.Select(group => new SharedParameterGroupDto { Id = group.Id, Name = group.Name }).ToList(),
            Parameters = file.Parameters.Select(parameter => new SharedParameterDto
            {
                Guid = parameter.Guid.ToString("D"),
                Name = parameter.Name,
                DataType = parameter.DataType,
                DataCategory = parameter.DataCategory,
                GroupId = parameter.GroupId,
                Visible = parameter.Visible,
                Description = parameter.Description,
                UserModifiable = parameter.UserModifiable,
                HideWhenNoValue = parameter.HideWhenNoValue
            }).ToList()
        };
    }
}
