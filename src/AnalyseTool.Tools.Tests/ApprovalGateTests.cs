using AnalyseTool.Tools.Pipelines;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AnalyseTool.Tools.Tests
{
    /// <summary>
    /// The gate between a model's proposal and the document. Its failure modes are asymmetric: a row
    /// wrongly parked costs someone a minute, a row wrongly accepted writes to the model unattended.
    /// Every case here is written from that side.
    /// </summary>
    [TestFixture]
    public sealed class ApprovalGateTests
    {
        private static ApprovalGate.Request Request(
            IEnumerable<object> items, string? field = null, ApprovalGate.AcceptRule? rule = null) =>
            new() { Items = items.ToList(), Field = field, AutoAccept = rule };

        private static IEnumerable<string?> Names(IReadOnlyList<object> rows) =>
            rows.Cast<JToken>().Select(t => (string?)t["name"]);

        private static string? Why(IReadOnlyList<object> rows, int index) =>
            (string?)((JToken)rows[index])["approval"]!["why"];

        [Test]
        public void No_rule_parks_everything()
        {
            // A gate whose rules were not filled in must not be a gate that is open. Note the run still
            // completes and the rows are still there — parked is a destination, not a failure.
            ApprovalResult result = ApprovalGate.Apply(
                Request([new { name = "a" }, new { name = "b" }]));

            Assert.That(result.Accepted, Is.Empty);
            Assert.That(result.ParkedCount, Is.EqualTo(2));
            Assert.That(Why(result.Parked, 0), Does.Contain("waits for a person"));
        }

        [Test]
        public void Every_row_ends_up_on_exactly_one_side()
        {
            ApprovalResult result = ApprovalGate.Apply(
                Request(
                    [new { mark = "AB_Wall_001" }, new { mark = "junk" }, new { mark = "CD_Slab_012" }],
                    "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^[A-Z]{2}_[A-Za-z0-9]+_\\d{3}$" }));

            Assert.That(result.AcceptedCount + result.ParkedCount, Is.EqualTo(3));
            Assert.That(result.AcceptedCount, Is.EqualTo(2));
        }

        [Test]
        public void A_parked_row_says_what_failed_and_against_what()
        {
            // The parked list goes into a CSV a person reads. "Rejected" tells them nothing; the value
            // and the rule together let them decide whether the row or the rule is wrong.
            ApprovalResult result = ApprovalGate.Apply(
                Request([new { mark = "junk" }], "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$" }));

            Assert.That(Why(result.Parked, 0), Does.Contain("junk").And.Contain("^K-\\d+$"));
        }

        [Test]
        public void A_row_missing_the_checked_field_is_parked_not_accepted()
        {
            // Absent is not "passes by default". This is the direction that writes to the model.
            ApprovalResult result = ApprovalGate.Apply(
                Request([new { name = "a" }], "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = ".*" }));

            Assert.That(result.Accepted, Is.Empty);
            Assert.That(Why(result.Parked, 0), Does.Contain("missing"));
        }

        [Test]
        public void MinConfidence_alone_is_refused()
        {
            // The load-bearing rule of the whole gate. Self-reported confidence is not calibrated and is
            // least reliable on exactly the rows that are unusual, so it can narrow an approval and never
            // grant one. Refused at configuration time rather than quietly honoured.
            Assert.That(
                () => ApprovalGate.Apply(
                    Request([new { name = "a" }], null,
                        new ApprovalGate.AcceptRule { MinConfidence = 0.9 })),
                Throws.InvalidOperationException.With.Message.Contains("only 'minConfidence'"));
        }

        [Test]
        public void MinConfidence_is_allowed_alongside_a_stronger_condition()
        {
            ApprovalResult result = ApprovalGate.Apply(
                Request(
                    [new { mark = "K-1", ai = new { confidence = 0.95 } },
                     new { mark = "K-2", ai = new { confidence = 0.40 } }],
                    "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$", MinConfidence = 0.9 }));

            Assert.That(result.AcceptedCount, Is.EqualTo(1));
            Assert.That(Why(result.Parked, 0), Does.Contain("0.4").And.Contain("0.9"));
        }

        [Test]
        public void A_row_with_no_confidence_fails_a_confidence_rule()
        {
            // An AiTransform row that was never answered has no ai.confidence. It must not slip through
            // a rule that asks for one.
            ApprovalResult result = ApprovalGate.Apply(
                Request([new { mark = "K-1" }], "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$", MinConfidence = 0.5 }));

            Assert.That(result.Accepted, Is.Empty);
            Assert.That(Why(result.Parked, 0), Does.Contain("no ai.confidence"));
        }

        [Test]
        public void Exceeding_maxItems_parks_everything_rather_than_an_arbitrary_subset()
        {
            // More changed than the pipeline expected, so the situation is not the one the author had in
            // mind. Letting an arbitrary first 50 through would bound the damage and hide that.
            ApprovalResult result = ApprovalGate.Apply(
                Request(
                    Enumerable.Range(0, 5).Select(i => (object)new { mark = $"K-{i}" }),
                    "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$", MaxItems = 3 }));

            Assert.That(result.Accepted, Is.Empty);
            Assert.That(result.ParkedCount, Is.EqualTo(5));
            Assert.That(Why(result.Parked, 0), Does.Contain("5").And.Contain("3"));
        }

        [Test]
        public void Within_maxItems_the_accepted_rows_flow()
        {
            ApprovalResult result = ApprovalGate.Apply(
                Request(
                    [new { name = "a", mark = "K-1" }, new { name = "b", mark = "junk" }],
                    "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$", MaxItems = 50 }));

            Assert.That(Names(result.Accepted), Is.EqualTo(new[] { "a" }));
            Assert.That(Names(result.Parked), Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void An_accepted_row_keeps_its_fields_and_carries_the_verdict()
        {
            // A write command binds ids straight off these rows, so nothing may be dropped in passing.
            ApprovalResult result = ApprovalGate.Apply(
                Request([new { id = 42, mark = "K-1" }], "mark",
                    new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$" }));

            JToken row = (JToken)result.Accepted[0];
            Assert.Multiple(() =>
            {
                Assert.That((int)row["id"]!, Is.EqualTo(42));
                Assert.That((bool)row["approval"]!["accepted"]!, Is.True);
            });
        }

        [Test]
        public void A_valueMatches_without_a_field_is_refused()
        {
            Assert.That(
                () => ApprovalGate.Apply(
                    Request([new { mark = "K-1" }], null,
                        new ApprovalGate.AcceptRule { ValueMatches = "^K-\\d+$" })),
                Throws.InvalidOperationException.With.Message.Contains("no 'field'"));
        }

        [Test]
        public void A_broken_regular_expression_is_refused_rather_than_matching_nothing()
        {
            // Silently matching nothing would park everything, which LOOKS like a cautious gate working
            // and is actually a rule that never ran.
            Assert.That(
                () => ApprovalGate.Apply(
                    Request([new { mark = "K-1" }], "mark",
                        new ApprovalGate.AcceptRule { ValueMatches = "^K-(\\d+" })),
                Throws.InvalidOperationException.With.Message.Contains("valid regular expression"));
        }
    }
}
