using AnalyseTool.Tools.Ai;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AnalyseTool.Tools.Tests
{
    /// <summary>
    /// The two halves of the node that are not the model: what it is shown, and where its answers land.
    ///
    /// The second is the dangerous one. A model that drops one row out of twenty-five shifts every
    /// answer after it onto the wrong element — and the output still has the right shape, the right
    /// count of fields and a plausible value in every cell, so neither a count nor a person reading the
    /// report would catch it. Matching by index rather than by position is the whole defence, and it is
    /// worth pinning.
    /// </summary>
    [TestFixture]
    public sealed class AiTransformTests
    {
        private static JArray Rows(params object[] rows) => JArray.FromObject(rows);

        private static Dictionary<int, JObject> Answers(params (int Index, object Answer)[] answers) =>
            answers.ToDictionary(a => a.Index, a => JObject.FromObject(a.Answer));

        // --- what the model is shown ---------------------------------------------------------------

        [Test]
        public void Every_row_is_sent_with_an_index_the_model_must_echo()
        {
            JArray projected = Project(Rows(new { name = "a" }, new { name = "b" }), 0, null);

            Assert.That(projected.Select(r => (int)r["index"]!), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void The_index_continues_across_batches()
        {
            // Batches are sent separately but the answers land in one table. Restarting the numbering per
            // batch would put batch two's answers onto batch one's rows.
            JArray projected = Project(Rows(new { name = "c" }), 25, null);

            Assert.That((int)projected[0]["index"]!, Is.EqualTo(25));
        }

        [Test]
        public void Only_the_named_fields_are_shown()
        {
            // Not a token-saving nicety: a row from GetFamilyTypeRows carries every type parameter, and
            // sending all of them to answer a question about names buries the question.
            JArray projected = Project(
                Rows(new { name = "a", category = "Walls", parameters = new { fireRating = "F90" } }),
                0,
                new List<string> { "name" });

            Assert.That(((JObject)projected[0]).Properties().Select(p => p.Name),
                Is.EqualTo(new[] { "index", "name" }));
        }

        [Test]
        public void A_named_field_that_is_absent_is_simply_not_shown()
        {
            JArray projected = Project(Rows(new { name = "a" }), 0, new List<string> { "name", "mark" });

            Assert.That(((JObject)projected[0]).Properties().Select(p => p.Name),
                Is.EqualTo(new[] { "index", "name" }));
        }

        [Test]
        public void A_list_of_bare_values_is_shown_as_a_value_field()
        {
            // Binding `items[*].name` hands this node strings, not rows.
            JArray projected = Project(Rows("Wall-01", "Wall-02"), 0, null);

            Assert.That((string?)projected[1]["value"], Is.EqualTo("Wall-02"));
            Assert.That((int)projected[1]["index"]!, Is.EqualTo(1));
        }

        // --- where the answers land ----------------------------------------------------------------

        [Test]
        public void An_answer_lands_on_the_row_its_index_names_not_on_its_position()
        {
            // The model returned the rows out of order and skipped one. Position-based merging would put
            // row 2's answer onto row 1 and leave the report confidently wrong.
            JArray merged = Merge(
                Rows(new { name = "a" }, new { name = "b" }, new { name = "c" }),
                Answers((2, new { index = 2, mark = "C" }), (0, new { index = 0, mark = "A" })),
                new List<string> { "mark" },
                "test-model");

            Assert.That(merged.Select(r => (string?)r["mark"]), Is.EqualTo(new[] { "A", null, "C" }));
            Assert.That(merged.Select(r => (bool)r["ai"]!["answered"]!),
                Is.EqualTo(new[] { true, false, true }));
        }

        [Test]
        public void An_unanswered_row_comes_back_unchanged_rather_than_disappearing()
        {
            // A shorter list that looks like data is the failure mode this codebase has been fixing all
            // week. The row stays, keeps its fields, and says it was not answered.
            JArray merged = Merge(
                Rows(new { name = "a", id = 7 }), new Dictionary<int, JObject>(),
                new List<string> { "mark" }, "test-model");

            Assert.That(merged, Has.Count.EqualTo(1));
            Assert.That((string?)merged[0]["name"], Is.EqualTo("a"));
            Assert.That((int)merged[0]["id"]!, Is.EqualTo(7));
            Assert.That((bool)merged[0]["ai"]!["answered"]!, Is.False);
        }

        [Test]
        public void A_half_answered_row_counts_as_unanswered_and_names_what_is_missing()
        {
            // Acting on a row whose new name arrived but whose reason did not is acting on something the
            // contract did not deliver. It has to be filterable, so it is not "answered".
            JArray merged = Merge(
                Rows(new { name = "a" }),
                Answers((0, new { index = 0, mark = "A" })),
                new List<string> { "mark", "reason" },
                "test-model");

            Assert.That((bool)merged[0]["ai"]!["answered"]!, Is.False);
            Assert.That(merged[0]["ai"]!["missing"]!.Select(t => (string?)t), Is.EqualTo(new[] { "reason" }));
        }

        [Test]
        public void Only_the_declared_fields_are_taken_from_the_answer()
        {
            // A model that invents an extra key must not be able to add a column nobody asked for — the
            // next node binds against the DECLARED shape, and a surprise field is at best noise.
            JArray merged = Merge(
                Rows(new { name = "a" }),
                Answers((0, new { index = 0, mark = "A", severity = "high" })),
                new List<string> { "mark" },
                "test-model");

            Assert.That(merged[0]["severity"], Is.Null);
            Assert.That((string?)merged[0]["mark"], Is.EqualTo("A"));
        }

        [Test]
        public void Provenance_rides_with_every_row()
        {
            // What the approval node's predicate reads, and what a person reads in the report.
            JArray merged = Merge(
                Rows(new { name = "a" }),
                Answers((0, new { index = 0, mark = "A", confidence = 0.92, why = "matches the standard" })),
                new List<string> { "mark" },
                "gpt-test");

            JToken ai = merged[0]["ai"]!;
            Assert.Multiple(() =>
            {
                Assert.That((double)ai["confidence"]!, Is.EqualTo(0.92).Within(0.0001));
                Assert.That((string?)ai["why"], Is.EqualTo("matches the standard"));
                Assert.That((string?)ai["model"], Is.EqualTo("gpt-test"));
            });
        }

        [Test]
        public void An_answer_may_overwrite_a_field_the_row_already_had()
        {
            // "Fix the marks" writes over `mark` on purpose. Naming a new field is how an author keeps
            // the original — the node does not decide that for them.
            JArray merged = Merge(
                Rows(new { mark = "old" }),
                Answers((0, new { index = 0, mark = "new" })),
                new List<string> { "mark" },
                "test-model");

            Assert.That((string?)merged[0]["mark"], Is.EqualTo("new"));
        }

        [Test]
        public void An_answer_for_a_row_that_does_not_exist_is_ignored()
        {
            // A model that returns index 99 for a batch of three has misunderstood. Nothing to attach it
            // to, and inventing a row for it would put a fabricated element into the pipeline.
            JArray merged = Merge(
                Rows(new { name = "a" }),
                Answers((0, new { index = 0, mark = "A" }), (99, new { index = 99, mark = "X" })),
                new List<string> { "mark" },
                "test-model");

            Assert.That(merged, Has.Count.EqualTo(1));
            Assert.That((string?)merged[0]["mark"], Is.EqualTo("A"));
        }

        // Reaching the internals through the same names the command uses, so a rename breaks the test
        // rather than silently leaving it testing nothing.
        private static JArray Project(JArray batch, int offset, List<string>? fields) =>
            AiTransform.Project(batch, offset, fields);

        private static JArray Merge(
            JArray rows, IReadOnlyDictionary<int, JObject> answers, List<string> produce, string model) =>
            AiTransform.Merge(rows, answers, produce, model);
    }
}
