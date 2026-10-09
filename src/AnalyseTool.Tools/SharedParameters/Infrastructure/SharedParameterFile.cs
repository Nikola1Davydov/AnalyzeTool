using System.Globalization;
using System.Text;

namespace AnalyseTool.Tools.SharedParameters
{
    /// <summary>
    /// Reads and writes Revit's shared parameter file — the tab-separated text Revit keeps behind
    /// Manage → Shared Parameters. Done by hand, not through Revit's DefinitionFile: that API only ADDS
    /// groups and parameters, and the editor also renames, moves, describes and deletes.
    ///
    /// The format is sections, each announced by a header row naming its columns:
    /// <code>
    /// *GROUP  ID    NAME
    /// GROUP   1     Common
    /// *PARAM  GUID  NAME  DATATYPE  DATACATEGORY  GROUP  VISIBLE  DESCRIPTION  USERMODIFIABLE  HIDEWHENNOVALUE
    /// PARAM   …
    /// </code>
    /// Columns are looked up by their header name, never by position, and a column this class does not
    /// know is carried through untouched — a newer Revit adding one must not lose it on our next save.
    /// The same holds for whole sections we do not know, and for the encoding (Revit writes UTF-16).
    /// Pure text: no Revit API, so it is tested without Revit (tier 1).
    /// </summary>
    internal sealed class SharedParameterFile
    {
        private static readonly string[] DefaultParamColumns =
            ["GUID", "NAME", "DATATYPE", "DATACATEGORY", "GROUP", "VISIBLE", "DESCRIPTION", "USERMODIFIABLE", "HIDEWHENNOVALUE"];

        private static readonly string[] DefaultHeaderComments =
            ["# This is a Revit shared parameter file.", "# Do not edit manually."];

        public List<string> Comments { get; } = new();
        public string MetaVersion { get; set; } = "2";
        public string MetaMinVersion { get; set; } = "1";
        public List<SharedParameterGroup> Groups { get; } = new();
        public List<SharedParameterDefinition> Parameters { get; } = new();

        /// <summary>Column order of the PARAM section as the file declared it (unknown ones included).</summary>
        public List<string> ParamColumns { get; } = new(DefaultParamColumns);

        /// <summary>Sections other than META/GROUP/PARAM, kept verbatim (header line + rows).</summary>
        public List<List<string>> UnknownSections { get; } = new();

        public Encoding Encoding { get; set; } = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        public static SharedParameterFile CreateEmpty()
        {
            SharedParameterFile file = new();
            file.Comments.AddRange(DefaultHeaderComments);
            return file;
        }

        public static SharedParameterFile Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using StreamReader reader = new(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            SharedParameterFile file = Parse(text);
            // No BOM means Revit did not write it (Revit always writes UTF-16 with a BOM); UTF-8 is the
            // honest guess then, and keeping it is safer than converting someone's hand-made file.
            file.Encoding = HasBom(bytes) ? reader.CurrentEncoding : new UTF8Encoding(false);
            return file;
        }

        private static bool HasBom(byte[] b) =>
            (b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF))) ||
            (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF);

        public static SharedParameterFile Parse(string text)
        {
            SharedParameterFile file = new();
            Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);
            List<string>? unknown = null;

            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.StartsWith('#'))
                {
                    file.Comments.Add(line);
                    continue;
                }

                string[] cells = line.Split('\t');
                if (cells[0].StartsWith('*'))
                {
                    string kind = cells[0][1..];
                    string[] headerColumns = cells[1..];
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
                        unknown = new List<string> { line };
                        file.UnknownSections.Add(unknown);
                    }
                    continue;
                }

                string rowKind = cells[0];
                if (!IsKnown(rowKind))
                {
                    // A row of a section we do not know — or one with no header at all: keep it as is.
                    if (unknown is null)
                    {
                        unknown = new List<string>();
                        file.UnknownSections.Add(unknown);
                    }
                    unknown.Add(line);
                    continue;
                }
                if (!headers.TryGetValue(rowKind, out string[]? columns))
                    throw new FormatException($"A {rowKind} row comes before its *{rowKind} header line.");

                Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < columns.Length; i++)
                    row[columns[i]] = i + 1 < cells.Length ? cells[i + 1] : string.Empty;

                switch (rowKind.ToUpperInvariant())
                {
                    case "META":
                        file.MetaVersion = Get(row, "VERSION") ?? file.MetaVersion;
                        file.MetaMinVersion = Get(row, "MINVERSION") ?? file.MetaMinVersion;
                        break;
                    case "GROUP":
                        if (!int.TryParse(Get(row, "ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int groupId))
                            throw new FormatException($"Group \"{Get(row, "NAME")}\" has no numeric ID.");
                        file.Groups.Add(new SharedParameterGroup { Id = groupId, Name = Get(row, "NAME") ?? string.Empty });
                        break;
                    case "PARAM":
                        file.Parameters.Add(ParseParam(row));
                        break;
                }
            }
            return file;
        }

        private static bool IsKnown(string kind) =>
            kind.Equals("META", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("PARAM", StringComparison.OrdinalIgnoreCase);

        private static string? Get(Dictionary<string, string> row, string column) =>
            row.TryGetValue(column, out string? value) ? value : null;

        private static SharedParameterDefinition ParseParam(Dictionary<string, string> row)
        {
            string guidText = Get(row, "GUID") ?? string.Empty;
            if (!Guid.TryParse(guidText, out Guid guid))
                throw new FormatException($"Parameter \"{Get(row, "NAME")}\" has an invalid GUID \"{guidText}\".");
            int.TryParse(Get(row, "GROUP"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int group);

            SharedParameterDefinition p = new()
            {
                Guid = guid,
                Name = Get(row, "NAME") ?? string.Empty,
                DataType = Get(row, "DATATYPE") ?? string.Empty,
                DataCategory = Get(row, "DATACATEGORY") ?? string.Empty,
                GroupId = group,
                Visible = Get(row, "VISIBLE") != "0",
                Description = Get(row, "DESCRIPTION") ?? string.Empty,
                UserModifiable = Get(row, "USERMODIFIABLE") != "0",
                HideWhenNoValue = Get(row, "HIDEWHENNOVALUE") == "1",
            };
            foreach ((string column, string value) in row)
                if (!DefaultParamColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                    p.ExtraColumns[column] = value;
            return p;
        }

        public string Serialize()
        {
            StringBuilder sb = new();
            foreach (string comment in Comments) sb.Append(comment).Append("\r\n");

            sb.Append("*META\tVERSION\tMINVERSION\r\n");
            sb.Append("META\t").Append(MetaVersion).Append('\t').Append(MetaMinVersion).Append("\r\n");

            sb.Append("*GROUP\tID\tNAME\r\n");
            foreach (SharedParameterGroup g in Groups)
                sb.Append("GROUP\t").Append(g.Id.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Clean(g.Name)).Append("\r\n");

            sb.Append("*PARAM\t").Append(string.Join('\t', ParamColumns)).Append("\r\n");
            foreach (SharedParameterDefinition p in Parameters)
            {
                sb.Append("PARAM");
                foreach (string column in ParamColumns) sb.Append('\t').Append(Clean(Cell(p, column)));
                sb.Append("\r\n");
            }

            foreach (List<string> section in UnknownSections)
                foreach (string line in section) sb.Append(line).Append("\r\n");
            return sb.ToString();
        }

        private static string Cell(SharedParameterDefinition p, string column) => column.ToUpperInvariant() switch
        {
            "GUID" => p.Guid.ToString("D"),
            "NAME" => p.Name,
            "DATATYPE" => p.DataType,
            "DATACATEGORY" => p.DataCategory,
            "GROUP" => p.GroupId.ToString(CultureInfo.InvariantCulture),
            "VISIBLE" => p.Visible ? "1" : "0",
            "DESCRIPTION" => p.Description,
            "USERMODIFIABLE" => p.UserModifiable ? "1" : "0",
            "HIDEWHENNOVALUE" => p.HideWhenNoValue ? "1" : "0",
            _ => p.ExtraColumns.TryGetValue(column, out string? v) ? v : string.Empty,
        };

        /// <summary>A tab or a line break inside a cell would split the row; Revit cannot read it back.</summary>
        private static string Clean(string value) =>
            value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

        public void Save(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // A copy of what was there before: this file is shared across projects, often across a team.
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, Serialize(), Encoding);
        }

        /// <summary>A file version fingerprint: an edit made elsewhere between load and save is caught
        /// instead of being overwritten with the stale copy the editor holds.</summary>
        public static string Stamp(string path)
        {
            FileInfo info = new(path);
            return info.Exists
                ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + info.Length.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        /// <summary>The problems that would make Revit refuse the file, or make it ambiguous. Empty = fine.</summary>
        public List<string> Validate()
        {
            List<string> problems = new();
            HashSet<int> groupIds = new();
            HashSet<string> groupNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (SharedParameterGroup g in Groups)
            {
                if (g.Id <= 0) problems.Add($"Group \"{g.Name}\" needs a positive ID.");
                if (!groupIds.Add(g.Id)) problems.Add($"Group ID {g.Id} is used twice.");
                if (string.IsNullOrWhiteSpace(g.Name)) problems.Add($"Group {g.Id} has no name.");
                else if (!groupNames.Add(g.Name.Trim())) problems.Add($"Group name \"{g.Name}\" is used twice.");
            }

            HashSet<Guid> guids = new();
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (SharedParameterDefinition p in Parameters)
            {
                string label = string.IsNullOrWhiteSpace(p.Name) ? p.Guid.ToString("D") : $"\"{p.Name}\"";
                if (string.IsNullOrWhiteSpace(p.Name)) problems.Add($"Parameter {label} has no name.");
                else if (!names.Add(p.Name.Trim())) problems.Add($"Parameter name {label} is used twice.");
                if (p.Guid == Guid.Empty) problems.Add($"Parameter {label} has no GUID.");
                else if (!guids.Add(p.Guid)) problems.Add($"GUID {p.Guid:D} is used twice.");
                if (string.IsNullOrWhiteSpace(p.DataType)) problems.Add($"Parameter {label} has no data type.");
                if (!groupIds.Contains(p.GroupId)) problems.Add($"Parameter {label} points to group {p.GroupId}, which does not exist.");
            }
            return problems;
        }
    }

    internal sealed class SharedParameterGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class SharedParameterDefinition
    {
        public Guid Guid { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>As the file spells it: "TEXT", "LENGTH", "YESNO"… or a spec id from newer Revit.</summary>
        public string DataType { get; set; } = string.Empty;
        /// <summary>For FAMILYTYPE parameters: the category id the type belongs to. Empty otherwise.</summary>
        public string DataCategory { get; set; } = string.Empty;
        public int GroupId { get; set; }
        public bool Visible { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public bool UserModifiable { get; set; } = true;
        public bool HideWhenNoValue { get; set; }
        /// <summary>Columns this version does not know, carried through on save.</summary>
        public Dictionary<string, string> ExtraColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
