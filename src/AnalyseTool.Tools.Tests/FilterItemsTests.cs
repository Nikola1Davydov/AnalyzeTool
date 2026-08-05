using AnalyseTool.Tools.Pipelines;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AnalyseTool.Tools.Tests
{
    /// <summary>
    /// Filter is the node a purge pipeline hangs off: what it keeps is what gets deleted. Every one of
    /// these cases is a way that decision can be silently wrong, and until now the only way to check any
    /// of them was to run a pipeline against a real model and read the JSON that came back.
    /// </summary>
    [TestFixture]
    public sealed class FilterItemsTests
    {
        private static FilterItems.Request Request(
            IEnumerable<object> items, string? match = null, params FilterItems.Condition[] where) =>
            new()
            {
                Items = items.ToList(),
                Match = match,
                Where = where.Length > 0 ? where.ToList() : null,
            };

        private static FilterItems.Condition Where(string field, string op, object? value = null) =>
            new() { Field = field, Op = op, Value = value };

        private static IEnumerable<string?> Names(FilterResult result) =>
            result.Items.Cast<JToken>().Select(t => (string?)t["name"]);

        [Test]
        public void No_conditions_passes_everything_through()
        {
            // Documented and deliberate: a node whose rules are not filled in yet must not break a
            // half-built pipeline. What makes it safe is the validator refusing this shape in front of a
            // command that changes the model — so if this ever became "keep nothing", that check would
            // be guarding a case that no longer exists while the real danger moved elsewhere.
            FilterResult result = FilterItems.Apply(
                Request([new { name = "a" }, new { name = "b" }]));

            Assert.That(result.Kept, Is.EqualTo(2));
            Assert.That(result.Dropped, Is.Zero);
            Assert.That(Names(result), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void A_numeric_zero_compares_as_a_number()
        {
            // The exact condition of "purge unused": instanceCount == 0. It is also the condition the
            // published schema once made impossible to express — `value` was declared as a Newtonsoft
            // type, so the generated schema forbade scalars and only `[0]` got through the wire, which
            // matched nothing. The wire is fixed; this is the behaviour that fix was protecting.
            FilterResult result = FilterItems.Apply(
                Request(
                    [new { name = "unused", instanceCount = 0 }, new { name = "used", instanceCount = 7 }],
                    null,
                    Where("instanceCount", "equals", 0)));

            Assert.That(Names(result), Is.EqualTo(new[] { "unused" }));
            Assert.That(result.Dropped, Is.EqualTo(1));
        }

        [Test]
        public void Kept_and_dropped_always_account_for_every_item()
        {
            // The counts are what a run receipt reports and what a person reads to decide whether the
            // filter did anything sane. If they ever stop summing to the input, they are worse than
            // absent: "kept 2, dropped 284" would be believed.
            FilterResult result = FilterItems.Apply(
                Request(
                    Enumerable.Range(0, 20).Select(i => (object)new { n = i }),
                    null,
                    Where("n", "lessThan", 5)));

            Assert.That(result.Kept + result.Dropped, Is.EqualTo(20));
            Assert.That(result.Kept, Is.EqualTo(result.Items.Count));
        }

        [Test]
        public void Match_all_requires_every_condition_and_match_any_requires_one()
        {
            object[] items =
            [
                new { name = "both", unused = true, loadable = true },
                new { name = "one", unused = true, loadable = false },
            ];

            FilterResult all = FilterItems.Apply(
                Request(items, "all", Where("unused", "equals", true), Where("loadable", "equals", true)));
            FilterResult any = FilterItems.Apply(
                Request(items, "any", Where("unused", "equals", true), Where("loadable", "equals", true)));

            Assert.That(Names(all), Is.EqualTo(new[] { "both" }));
            Assert.That(Names(any), Is.EqualTo(new[] { "both", "one" }));
        }

        [Test]
        public void Match_defaults_to_all()
        {
            // "all" is the safe default in front of a purge: an unset value that meant "any" would widen
            // the selection, which is the direction that deletes things nobody asked to delete.
            FilterResult result = FilterItems.Apply(
                Request(
                    [new { name = "a", x = 1, y = 2 }],
                    null,
                    Where("x", "equals", 1),
                    Where("y", "equals", 99)));

            Assert.That(result.Kept, Is.Zero);
        }

        [Test]
        public void An_unknown_operator_keeps_nothing()
        {
            // A typo has to be visible. The alternative — treating it as "no opinion" and passing the
            // item — turns a misspelled operator into a filter that silently selects everything, which
            // in front of PurgeFamilies is the difference between two families and all of them.
            FilterResult result = FilterItems.Apply(
                Request([new { name = "a", x = 1 }], null, Where("x", "equalz", 1)));

            Assert.That(result.Kept, Is.Zero);
            Assert.That(result.Dropped, Is.EqualTo(1));
        }

        [Test]
        public void A_missing_field_fails_every_operator_except_notExists()
        {
            object[] items = [new { name = "no-mark" }];

            Assert.Multiple(() =>
            {
                // Not "null equals null, therefore true" — a row that does not have the field at all is
                // not a row whose field is empty, and only notExists may say yes to it.
                Assert.That(FilterItems.Apply(Request(items, null, Where("mark", "equals", ""))).Kept, Is.Zero);
                Assert.That(FilterItems.Apply(Request(items, null, Where("mark", "notEquals", "x"))).Kept, Is.Zero);
                Assert.That(FilterItems.Apply(Request(items, null, Where("mark", "contains", "x"))).Kept, Is.Zero);
                Assert.That(FilterItems.Apply(Request(items, null, Where("mark", "exists"))).Kept, Is.Zero);
                Assert.That(FilterItems.Apply(Request(items, null, Where("mark", "notExists"))).Kept, Is.EqualTo(1));
            });
        }

        [Test]
        public void An_explicit_null_counts_as_not_existing()
        {
            // A parameter that is present but unset arrives as null. For "which elements are missing a
            // Mark" — the shape of every parameter-validation pipeline — absent and null are the same
            // answer, and splitting them would silently halve the report.
            FilterResult result = FilterItems.Apply(
                Request([new { name = "a", mark = (string?)null }], null, Where("mark", "notExists")));

            Assert.That(result.Kept, Is.EqualTo(1));
        }

        [Test]
        public void Numbers_compare_as_numbers_not_as_text()
        {
            // Ordinal text comparison puts "10" before "9". A pipeline filtering "instanceCount greater
            // than 9" would then drop everything in double figures — the rows it most wanted.
            FilterResult result = FilterItems.Apply(
                Request(
                    [new { name = "ten", n = 10 }, new { name = "nine", n = 9 }],
                    null,
                    Where("n", "greaterThan", 9)));

            Assert.That(Names(result), Is.EqualTo(new[] { "ten" }));
        }

        [Test]
        public void A_dotted_field_reaches_into_the_row()
        {
            FilterResult result = FilterItems.Apply(
                Request(
                    [new { name = "a", parameters = new { mark = "K-01" } },
                     new { name = "b", parameters = new { mark = "" } }],
                    null,
                    Where("parameters.mark", "contains", "K-")));

            Assert.That(Names(result), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Comparison_is_case_insensitive()
        {
            // Revit names are typed by people. A filter that distinguished "Wall" from "wall" would be
            // correct and useless.
            FilterResult result = FilterItems.Apply(
                Request([new { name = "Wall" }], null, Where("name", "equals", "wall")));

            Assert.That(result.Kept, Is.EqualTo(1));
        }

        [Test]
        public void An_empty_input_produces_an_empty_result_rather_than_failing()
        {
            // Run "purge unused" twice and the second run legitimately has nothing to filter. The engine
            // treats that as an answer rather than a broken binding, and the node has to agree.
            FilterResult result = FilterItems.Apply(
                Request([], null, Where("instanceCount", "equals", 0)));

            Assert.That(result.Items, Is.Empty);
            Assert.That(result.Kept, Is.Zero);
            Assert.That(result.Dropped, Is.Zero);
        }

        [Test]
        public void Rows_are_handed_on_whole_and_in_order()
        {
            // The next node binds a path INTO these rows — `items[*].id`. Anything that reshaped or
            // reordered them here would break a binding written against the source node's schema.
            FilterResult result = FilterItems.Apply(
                Request(
                    [new { id = 3, keep = true }, new { id = 1, keep = false }, new { id = 2, keep = true }],
                    null,
                    Where("keep", "equals", true)));

            Assert.That(result.Items.Cast<JToken>().Select(t => (int)t["id"]!), Is.EqualTo(new[] { 3, 2 }));
            Assert.That(((JToken)result.Items[0])["keep"], Is.Not.Null, "the whole row travels, not just the matched field");
        }
    }
}
