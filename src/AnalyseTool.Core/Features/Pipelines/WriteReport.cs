using AnalyseTool.Core.Common;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>
    /// The sink. Writes a list of rows to a CSV file a person can open.
    ///
    /// <para>Without one, a read-only pipeline has nowhere to put its answer. Every checking routine ends
    /// the same way — "which elements are missing a Mark", "which types are unused" — and the result went
    /// into a JSON blob in a panel, which nobody forwards and nobody keeps. The purge run that collected
    /// ~340 resolved warnings is the same gap from the other side: data produced, read by no one.</para>
    ///
    /// <para>Rows in, file out, nothing else. It takes ANY list of objects, so it serves both cases with
    /// one node: bind a Filter's <c>items</c> to report what a check found, or a purge's <c>warnings</c>
    /// to report what a write ran into.</para>
    ///
    /// <para>The file name always carries a timestamp and NOTHING overwrites. A report is evidence: last
    /// month's is what this month's is compared against, and silently replacing it is the same class of
    /// mistake as a save that re-serialises the author's file.</para>
    ///
    /// <para>CSV rather than a spreadsheet library — no dependency, opens in Excel, and readable in a text
    /// editor when it does not. The separator is a parameter because there is no correct default: Excel
    /// splits on the Windows list separator, which is a comma on an English machine and a semicolon on a
    /// German or Russian one, so the same file is a table on one desk and a single column on the next.
    /// The <c>sep=</c> first line settles it for Excel regardless of locale.</para>
    /// </summary>
    [RevitCommand(
        Description = "Writes rows to a CSV report in the AnalyseTool reports folder and returns its path. " +
                      "Payload: { items: [...], name, title, columns: [\"a\",\"b\"], separator }. " +
                      "Takes any list of objects — bind a Filter's 'items' to report what a check found, " +
                      "or a purge's 'warnings' to report what a write ran into. Columns default to every " +
                      "field the rows have, in first-seen order; nested values are written as JSON. " +
                      "The file name carries a timestamp and never overwrites an earlier report. " +
                      "Reads and writes NOTHING in the Revit model. Returns { path, rows, columns }.",
        ReadOnly = true,
        InputType = typeof(WriteReport.Request),
        OutputType = typeof(ReportResult))]
    internal sealed class WriteReport : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            JArray rows = req.Items is { Count: > 0 } ? JArray.FromObject(req.Items) : new JArray();
            List<string> columns = Columns(rows, req.Columns);
            string separator = string.IsNullOrEmpty(req.Separator) ? ";" : req.Separator!;

            Directory.CreateDirectory(PathProvider.ReportsRoot);
            string path = Path.Combine(
                PathProvider.ReportsRoot,
                $"{SafeName(req.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            // UTF-8 WITH a BOM: without it Excel reads the file as the machine's ANSI codepage, and every
            // Cyrillic or umlaut family name in the report arrives as mojibake.
            File.WriteAllText(
                path,
                Render(rows, columns, separator, req.Title),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Log.Information("Report written: {Path} ({Rows} row(s))", path, rows.Count);
            return Task.FromResult<object?>(new ReportResult(path, rows.Count, columns));
        }

        /// <summary>
        /// The file's whole content, as a function of the rows.
        ///
        /// <para>Separated from the writing so it can be tested. Everything that can silently ruin a
        /// report lives here — a family name containing the separator shifts every column after it by
        /// one, and a report that is subtly misaligned is worse than one that failed to appear.</para>
        /// </summary>
        internal static string Render(
            JArray rows, IReadOnlyList<string> columns, string separator, string? title)
        {
            StringBuilder text = new();

            text.Append("sep=").Append(separator).Append('\n');
            if (!string.IsNullOrWhiteSpace(title)) text.Append(Escape(title!, separator)).Append('\n');

            text.Append(string.Join(separator, columns.Select(c => Escape(c, separator)))).Append('\n');

            foreach (JToken row in rows)
                text.Append(string.Join(separator, columns.Select(c => Escape(Cell(row, c), separator))))
                    .Append('\n');

            return text.ToString();
        }

        /// <summary>The requested columns, or every field the rows have in first-seen order. Union rather
        /// than the first row's keys: rows from different sources need not agree, and a column that exists
        /// on only some of them is exactly the kind of thing a check is looking for.</summary>
        internal static List<string> Columns(JArray rows, List<string>? requested)
        {
            if (requested is { Count: > 0 }) return requested;

            List<string> columns = new();
            foreach (JToken row in rows)
                if (row is JObject o)
                    foreach (JProperty property in o.Properties())
                        if (!columns.Contains(property.Name, StringComparer.Ordinal))
                            columns.Add(property.Name);

            // A list of bare values (ids, names) has no fields at all; one column, so it still lands.
            return columns.Count > 0 ? columns : new List<string> { "value" };
        }

        private static string Cell(JToken row, string column)
        {
            if (row is not JObject) return column == "value" ? Text(row) : string.Empty;

            // A dotted column reaches into the row, the same way a Filter condition does — so a report can
            // name "parameters.mark" without the pipeline having to flatten anything first.
            JToken? value = row.SelectToken(column);
            return value is null ? string.Empty : Text(value);
        }

        private static string Text(JToken value) => value.Type switch
        {
            JTokenType.Null => string.Empty,
            // A nested array or object has no cell form. Written as compact JSON rather than as
            // "System.Object[]" or an empty cell, so the information survives into the file.
            JTokenType.Array or JTokenType.Object => value.ToString(Formatting.None),
            JTokenType.Float or JTokenType.Integer => value.ToObject<decimal>().ToString(CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        /// <summary>RFC 4180 quoting. A family name with a comma, a quote or a newline in it is ordinary in
        /// Revit and would otherwise shift every following column by one.</summary>
        private static string Escape(string value, string separator)
        {
            bool needsQuotes = value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                               || value.Contains(separator, StringComparison.Ordinal);

            return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        private static string SafeName(string? requested)
        {
            string cleaned = new((requested ?? string.Empty)
                .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-')
                .ToArray());

            cleaned = cleaned.Trim();
            if (cleaned.Length > 64) cleaned = cleaned[..64].Trim();

            // Unnamed is not an error here, unlike a pipeline: a report is an output, and refusing to
            // write one because nobody titled it would lose the run's only result.
            return cleaned.Length == 0 ? "report" : cleaned;
        }

        internal sealed class Request
        {
            [Description("The rows to write. Usually bound from a Filter's 'items' or a write command's 'warnings'.")]
            public List<object>? Items { get; set; }

            [Description("File name stem. A timestamp is appended and nothing is overwritten. Default \"report\".")]
            public string? Name { get; set; }

            [Description("Optional heading written above the column row.")]
            public string? Title { get; set; }

            [Description("Columns to write, in order. May be dotted paths (\"parameters.mark\"). " +
                         "Empty means every field the rows have, in first-seen order.")]
            public List<string>? Columns { get; set; }

            [Description("Column separator. Default \";\" — Excel splits on the Windows list separator, " +
                         "which differs by locale; the file carries a sep= line so Excel obeys this either way.")]
            public string? Separator { get; set; }
        }
    }

    internal sealed record ReportResult(
        [property: JsonProperty("path")] string Path,
        [property: JsonProperty("rows")] int Rows,
        [property: JsonProperty("columns")] IReadOnlyList<string> Columns);
}
