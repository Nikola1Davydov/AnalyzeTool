using AnalyseTool.Core.Features.Pipelines;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AnalyseTool.Core.Tests
{
    /// <summary>
    /// The report is the only thing a checking pipeline leaves behind, and the ways it goes wrong are
    /// quiet ones: a name containing the separator shifts every column after it by one, and nobody
    /// reading the file notices that the Mark column now holds a category. A report that is subtly
    /// misaligned is worse than one that failed to appear.
    /// </summary>
    [TestFixture]
    public sealed class ExportToCsvTests
    {
        private static JArray Rows(params object[] rows) => JArray.FromObject(rows);

        private static string[] Lines(string csv) =>
            csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        [Test]
        public void Columns_default_to_the_union_of_every_field_in_first_seen_order()
        {
            // Union, not the first row's keys: rows from two sources need not agree, and a field only
            // some of them carry is exactly what a check is looking for. Dropping it would make the
            // report agree with whichever row happened to be first.
            List<string> columns = ExportToCsv.Columns(
                Rows(new { id = 1, name = "a" }, new { id = 2, mark = "K-01" }), null);

            Assert.That(columns, Is.EqualTo(new[] { "id", "name", "mark" }));
        }

        [Test]
        public void Requested_columns_are_honoured_in_the_order_given()
        {
            List<string> columns = ExportToCsv.Columns(
                Rows(new { id = 1, name = "a" }), new List<string> { "name", "id" });

            Assert.That(columns, Is.EqualTo(new[] { "name", "id" }));
        }

        [Test]
        public void A_list_of_bare_values_still_produces_a_column()
        {
            // Binding `items[*].id` hands this node a list of numbers, not of rows. One column named
            // "value" rather than a file with a header and no data.
            List<string> columns = ExportToCsv.Columns(Rows(1, 2, 3), null);
            string csv = ExportToCsv.Render(Rows(1, 2, 3), columns, ";", null);

            Assert.That(columns, Is.EqualTo(new[] { "value" }));
            Assert.That(Lines(csv), Is.EqualTo(new[] { "sep=;", "value", "1", "2", "3" }));
        }

        [Test]
        public void A_value_containing_the_separator_is_quoted()
        {
            // "Fenster, doppelt" is an ordinary Revit family name. Unquoted it becomes two cells and
            // every column after it in that row is wrong.
            string csv = ExportToCsv.Render(
                Rows(new { name = "Fenster, doppelt", mark = "K-01" }), new[] { "name", "mark" }, ";", null);

            Assert.That(Lines(csv)[2], Is.EqualTo("\"Fenster, doppelt\";K-01"),
                "the comma is inside the value, and the separator here is a semicolon");

            string comma = ExportToCsv.Render(
                Rows(new { name = "Fenster, doppelt" }), new[] { "name" }, ",", null);

            Assert.That(Lines(comma)[2], Is.EqualTo("\"Fenster, doppelt\""));
        }

        [Test]
        public void Quotes_and_newlines_survive()
        {
            string csv = ExportToCsv.Render(
                Rows(new { note = "he said \"no\"\nthen left" }), new[] { "note" }, ";", null);

            Assert.That(csv, Does.Contain("\"he said \"\"no\"\"\nthen left\""));
        }

        [Test]
        public void A_missing_field_is_an_empty_cell_not_a_missing_one()
        {
            // The row count and the column count have to stay rectangular, or every following row reads
            // as shifted in whatever opens the file.
            string csv = ExportToCsv.Render(
                Rows(new { id = 1 }), new[] { "id", "mark" }, ";", null);

            Assert.That(Lines(csv)[2], Is.EqualTo("1;"));
        }

        [Test]
        public void A_nested_value_is_written_as_json_rather_than_lost()
        {
            // A purge's warnings carry `elementIds: [123, 456]`. An empty cell there would drop the only
            // part of the warning anyone can act on.
            string csv = ExportToCsv.Render(
                Rows(new { description = "w", elementIds = new[] { 123, 456 } }),
                new[] { "description", "elementIds" },
                ";",
                null);

            Assert.That(Lines(csv)[2], Is.EqualTo("w;[123,456]"));
        }

        [Test]
        public void Numbers_are_written_invariantly()
        {
            // On a German or Russian machine the current culture writes 1,5 — which is the separator on
            // that same machine, so the number would split itself across two columns.
            string csv = ExportToCsv.Render(Rows(new { area = 1.5 }), new[] { "area" }, ";", null);

            Assert.That(Lines(csv)[2], Is.EqualTo("1.5"));
        }

        [Test]
        public void A_dotted_column_reaches_into_the_row()
        {
            string csv = ExportToCsv.Render(
                Rows(new { name = "a", parameters = new { mark = "K-01" } }),
                new[] { "name", "parameters.mark" },
                ";",
                null);

            Assert.That(Lines(csv)[2], Is.EqualTo("a;K-01"));
        }

        [Test]
        public void The_sep_line_comes_first_and_a_title_sits_above_the_columns()
        {
            string csv = ExportToCsv.Render(
                Rows(new { id = 1 }), new[] { "id" }, ";", "Types without a Mark");

            Assert.That(Lines(csv), Is.EqualTo(new[] { "sep=;", "Types without a Mark", "id", "1" }));
        }

        [Test]
        public void No_rows_still_writes_the_header()
        {
            // "Nothing failed the check" is a result worth having on paper, and an empty file cannot be
            // told apart from a run that never happened.
            string csv = ExportToCsv.Render(new JArray(), new[] { "id", "mark" }, ";", "Clean");

            Assert.That(Lines(csv), Is.EqualTo(new[] { "sep=;", "Clean", "id;mark" }));
        }
    }
}
