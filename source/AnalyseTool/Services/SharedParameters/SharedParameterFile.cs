using System.Globalization;
using System.IO;
using System.Text;

namespace AnalyseTool.Services.SharedParameters;

/// <summary>
///     Reads and writes Revit's shared parameter file — the tab-separated text behind Manage → Shared Parameters. Done by
///     hand, not through Revit's <c>DefinitionFile</c>: that API only adds, and the editor also renames, moves, describes
///     and deletes.
/// </summary>
/// <remarks>
///     The format is sections, each announced by a header row naming its columns:
///     <code>
///     *GROUP  ID    NAME
///     GROUP   1     Common
///     *PARAM  GUID  NAME  DATATYPE  DATACATEGORY  GROUP  VISIBLE  DESCRIPTION  USERMODIFIABLE  HIDEWHENNOVALUE
///     PARAM   …
///     </code>
///     Columns are looked up by their header name, never by position, and a column this class does not know is carried
///     through untouched — a newer Revit adding one must not lose it on our next save. The same holds for whole sections we
///     do not know, and for the encoding (Revit writes UTF-16). Pure text: tested without Revit.
/// </remarks>
public sealed class SharedParameterFile
{
    private static readonly string[] DefaultParamColumns =
        ["GUID", "NAME", "DATATYPE", "DATACATEGORY", "GROUP", "VISIBLE", "DESCRIPTION", "USERMODIFIABLE", "HIDEWHENNOVALUE"];

    private static readonly string[] DefaultHeaderComments =
        ["# This is a Revit shared parameter file.", "# Do not edit manually."];

    /// <summary>The comment lines at the top, <c>#</c> included.</summary>
    public List<string> Comments { get; } = [];

    /// <summary>The META version.</summary>
    public string MetaVersion { get; set; } = "2";

    /// <summary>The META minimal version.</summary>
    public string MetaMinVersion { get; set; } = "1";

    /// <summary>The groups.</summary>
    public List<SharedParameterGroup> Groups { get; } = [];

    /// <summary>The parameters.</summary>
    public List<SharedParameterDefinition> Parameters { get; } = [];

    /// <summary>The column order of the PARAM section as the file declared it, unknown columns included.</summary>
    public List<string> ParamColumns { get; } = [..DefaultParamColumns];

    /// <summary>Sections other than META, GROUP and PARAM, kept verbatim (header line and rows).</summary>
    public List<List<string>> UnknownSections { get; } = [];

    /// <summary>The encoding to write; UTF-16 with a BOM, as Revit writes, for a new file.</summary>
    public Encoding Encoding { get; set; } = new UnicodeEncoding(false, true);

    /// <summary>A new, empty file with Revit's header comments.</summary>
    public static SharedParameterFile CreateEmpty()
    {
        var file = new SharedParameterFile();
        file.Comments.AddRange(DefaultHeaderComments);
        return file;
    }

    /// <summary>Reads a file and remembers its encoding.</summary>
    /// <exception cref="FormatException">The file is not a shared parameter file.</exception>
    public static SharedParameterFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var file = Parse(text);
        // No BOM means Revit did not write it; UTF-8 is the honest guess, and keeping it is safer than converting a hand-made file
        file.Encoding = HasBom(bytes) ? reader.CurrentEncoding : new UTF8Encoding(false);
        return file;
    }

    /// <summary>Parses the text of a file.</summary>
    /// <exception cref="FormatException">A row comes before its header, a group has no numeric id, or a GUID is invalid.</exception>
    public static SharedParameterFile Parse(string text)
    {
        var file = new SharedParameterFile();
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        List<string>? unknown = null;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                file.Comments.Add(line);
                continue;
            }

            var cells = line.Split('\t');
            if (cells[0].StartsWith('*'))
            {
                var kind = cells[0][1..];
                var headerColumns = cells[1..];
                headers[kind] = headerColumns;
                if (IsKnown(kind))
                {
                    unknown = null;
                    if (kind.Equals("PARAM", StringComparison.OrdinalIgnoreCase))
                    {
                        file.ParamColumns.Clear();
                        file.ParamColumns.AddRange(headerColumns);
                    }
                }
                else
                {
                    unknown = [line];
                    file.UnknownSections.Add(unknown);
                }

                continue;
            }

            var rowKind = cells[0];
            if (!IsKnown(rowKind))
            {
                // A row of a section we do not know, or one with no header at all: keep it as is
                if (unknown is null)
                {
                    unknown = [];
                    file.UnknownSections.Add(unknown);
                }

                unknown.Add(line);
                continue;
            }

            if (!headers.TryGetValue(rowKind, out var columns))
            {
                throw new FormatException($"A {rowKind} row comes before its *{rowKind} header line.");
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++)
            {
                row[columns[i]] = i + 1 < cells.Length ? cells[i + 1] : string.Empty;
            }

            switch (rowKind.ToUpperInvariant())
            {
                case "META":
                    file.MetaVersion = Get(row, "VERSION") ?? file.MetaVersion;
                    file.MetaMinVersion = Get(row, "MINVERSION") ?? file.MetaMinVersion;
                    break;
                case "GROUP":
                    if (!int.TryParse(Get(row, "ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupId))
                    {
                        throw new FormatException($"Group \"{Get(row, "NAME")}\" has no numeric ID.");
                    }

                    file.Groups.Add(new SharedParameterGroup { Id = groupId, Name = Get(row, "NAME") ?? string.Empty });
                    break;
                case "PARAM":
                    file.Parameters.Add(ParseParam(row));
                    break;
            }
        }

        return file;
    }

    /// <summary>The text of the file, rows ended with CRLF as Revit writes them.</summary>
    public string Serialize()
    {
        var builder = new StringBuilder();
        foreach (var comment in Comments)
        {
            builder.Append(comment).Append("\r\n");
        }

        builder.Append("*META\tVERSION\tMINVERSION\r\n");
        builder.Append("META\t").Append(MetaVersion).Append('\t').Append(MetaMinVersion).Append("\r\n");

        builder.Append("*GROUP\tID\tNAME\r\n");
        foreach (var group in Groups)
        {
            builder.Append("GROUP\t").Append(group.Id.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Clean(group.Name)).Append("\r\n");
        }

        builder.Append("*PARAM\t").Append(string.Join('\t', ParamColumns)).Append("\r\n");
        foreach (var parameter in Parameters)
        {
            builder.Append("PARAM");
            foreach (var column in ParamColumns)
            {
                builder.Append('\t').Append(Clean(Cell(parameter, column)));
            }

            builder.Append("\r\n");
        }

        foreach (var line in UnknownSections.SelectMany(section => section))
        {
            builder.Append(line).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>Writes the file in its encoding; the previous file stays as <c>.bak</c> — this file is shared across projects, often across a team.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
        }

        File.WriteAllText(path, Serialize(), Encoding);
    }

    /// <summary>A fingerprint of the file version: an edit made elsewhere between load and save is caught.</summary>
    public static string Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + info.Length.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    /// <summary>The problems that would make Revit refuse the file, or make it ambiguous; empty when it is fine.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        var groupIds = new HashSet<int>();
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            if (group.Id <= 0)
            {
                problems.Add($"Group \"{group.Name}\" needs a positive ID.");
            }

            if (!groupIds.Add(group.Id))
            {
                problems.Add($"Group ID {group.Id} is used twice.");
            }

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                problems.Add($"Group {group.Id} has no name.");
            }
            else if (!groupNames.Add(group.Name.Trim()))
            {
                problems.Add($"Group name \"{group.Name}\" is used twice.");
            }
        }

        var guids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in Parameters)
        {
            var label = string.IsNullOrWhiteSpace(parameter.Name) ? parameter.Guid.ToString("D") : $"\"{parameter.Name}\"";
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                problems.Add($"Parameter {label} has no name.");
            }
            else if (!names.Add(parameter.Name.Trim()))
            {
                problems.Add($"Parameter name {label} is used twice.");
            }

            if (parameter.Guid == Guid.Empty)
            {
                problems.Add($"Parameter {label} has no GUID.");
            }
            else if (!guids.Add(parameter.Guid))
            {
                problems.Add($"GUID {parameter.Guid:D} is used twice.");
            }

            if (string.IsNullOrWhiteSpace(parameter.DataType))
            {
                problems.Add($"Parameter {label} has no data type.");
            }

            if (!groupIds.Contains(parameter.GroupId))
            {
                problems.Add($"Parameter {label} points to group {parameter.GroupId}, which does not exist.");
            }
        }

        return problems;
    }

    private static bool HasBom(byte[] bytes)
    {
        return (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) ||
               (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    private static bool IsKnown(string kind)
    {
        return kind.Equals("META", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("PARAM", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Get(Dictionary<string, string> row, string column)
    {
        return row.GetValueOrDefault(column);
    }

    private static SharedParameterDefinition ParseParam(Dictionary<string, string> row)
    {
        var guidText = Get(row, "GUID") ?? string.Empty;
        if (!Guid.TryParse(guidText, out var guid))
        {
            throw new FormatException($"Parameter \"{Get(row, "NAME")}\" has an invalid GUID \"{guidText}\".");
        }

        int.TryParse(Get(row, "GROUP"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group);

        var parameter = new SharedParameterDefinition
        {
            Guid = guid,
            Name = Get(row, "NAME") ?? string.Empty,
            DataType = Get(row, "DATATYPE") ?? string.Empty,
            DataCategory = Get(row, "DATACATEGORY") ?? string.Empty,
            GroupId = group,
            Visible = Get(row, "VISIBLE") != "0",
            Description = Get(row, "DESCRIPTION") ?? string.Empty,
            UserModifiable = Get(row, "USERMODIFIABLE") != "0",
            HideWhenNoValue = Get(row, "HIDEWHENNOVALUE") == "1"
        };

        foreach (var (column, value) in row)
        {
            if (!DefaultParamColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                parameter.ExtraColumns[column] = value;
            }
        }

        return parameter;
    }

    private static string Cell(SharedParameterDefinition parameter, string column)
    {
        return column.ToUpperInvariant() switch
        {
            "GUID" => parameter.Guid.ToString("D"),
            "NAME" => parameter.Name,
            "DATATYPE" => parameter.DataType,
            "DATACATEGORY" => parameter.DataCategory,
            "GROUP" => parameter.GroupId.ToString(CultureInfo.InvariantCulture),
            "VISIBLE" => parameter.Visible ? "1" : "0",
            "DESCRIPTION" => parameter.Description,
            "USERMODIFIABLE" => parameter.UserModifiable ? "1" : "0",
            "HIDEWHENNOVALUE" => parameter.HideWhenNoValue ? "1" : "0",
            _ => parameter.ExtraColumns.GetValueOrDefault(column) ?? string.Empty
        };
    }

    /// <summary>A tab or a line break inside a cell would split the row; Revit could not read it back.</summary>
    private static string Clean(string value)
    {
        return value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
    }
}

/// <summary>A group of a shared parameter file.</summary>
public sealed class SharedParameterGroup
{
    /// <summary>The group number.</summary>
    public int Id { get; set; }

    /// <summary>The group name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A parameter definition of a shared parameter file.</summary>
public sealed class SharedParameterDefinition
{
    /// <summary>The GUID.</summary>
    public Guid Guid { get; set; }

    /// <summary>The name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>As the file spells it: <c>TEXT</c>, <c>LENGTH</c>, <c>YESNO</c>… or a spec id of a newer Revit.</summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>For FAMILYTYPE parameters the category id of the type; empty otherwise.</summary>
    public string DataCategory { get; set; } = string.Empty;

    /// <summary>The group number.</summary>
    public int GroupId { get; set; }

    /// <summary>Whether the parameter is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The tooltip.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether users may edit the value.</summary>
    public bool UserModifiable { get; set; } = true;

    /// <summary>Whether Revit hides the parameter without a value.</summary>
    public bool HideWhenNoValue { get; set; }

    /// <summary>Columns this version does not know, carried through on save.</summary>
    public Dictionary<string, string> ExtraColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
}
