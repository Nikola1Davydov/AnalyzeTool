using System.Collections.Generic;
using IdsSharp;
using NUnit.Framework;

namespace IdsSharp.Tests
{
    /// <summary>
    /// Where the library is right or wrong. Every other part decides which values to look at; this
    /// decides whether one passes, and a mistake here reports a clean model and a broken one with
    /// equal confidence.
    /// </summary>
    [TestFixture]
    public sealed class ValueRestrictionTests
    {
        [Test]
        public void A_null_value_never_matches()
        {
            // "The property is absent" and "the property is there and wrong" are different findings.
            // Collapsing them loses the one people most want: an element with no value at all.
            Assert.Multiple(() =>
            {
                Assert.That(ValueRestriction.Exact("A").Matches(null), Is.False);
                Assert.That(ValueRestriction.Create(pattern: ".*").Matches(null), Is.False);
                Assert.That(ValueRestriction.Create().Matches(null), Is.False);
            });
        }

        [Test]
        public void An_empty_restriction_accepts_any_value_but_not_none()
        {
            ValueRestriction any = ValueRestriction.Create();

            Assert.That(any.Matches(""), Is.True);
            Assert.That(any.Matches("anything"), Is.True);
        }

        [Test]
        public void A_pattern_is_anchored_to_the_whole_value()
        {
            // XSD patterns match the entire value; .NET's do not. Without anchoring, "^K-" would accept
            // "XK-01", and a naming standard would pass on names that violate it — a silent false clean.
            ValueRestriction restriction = ValueRestriction.Create(pattern: "K-[0-9]{3}");

            Assert.Multiple(() =>
            {
                Assert.That(restriction.Matches("K-001"), Is.True);
                Assert.That(restriction.Matches("XK-001"), Is.False, "a prefix must not be ignored");
                Assert.That(restriction.Matches("K-001X"), Is.False, "a suffix must not be ignored");
                Assert.That(restriction.Matches("K-0011"), Is.False);
            });
        }

        [Test]
        public void An_enumeration_is_exact_and_case_sensitive()
        {
            // IFC enumerated values are upper-case tokens; accepting "external" for "EXTERNAL" would
            // hide a real data problem, since the exported IFC would not accept it either.
            ValueRestriction restriction =
                ValueRestriction.Create(enumeration: new List<string> { "EXTERNAL", "INTERNAL" });

            Assert.Multiple(() =>
            {
                Assert.That(restriction.Matches("EXTERNAL"), Is.True);
                Assert.That(restriction.Matches("external"), Is.False);
                Assert.That(restriction.Matches("EXTERNA"), Is.False);
            });
        }

        [Test]
        public void Bounds_compare_numerically_and_respect_inclusivity()
        {
            ValueRestriction inclusive = ValueRestriction.Create(
                minimum: new ValueRestriction.Bound(2m, true),
                maximum: new ValueRestriction.Bound(10m, true));

            ValueRestriction exclusive = ValueRestriction.Create(
                minimum: new ValueRestriction.Bound(2m, false),
                maximum: new ValueRestriction.Bound(10m, false));

            Assert.Multiple(() =>
            {
                Assert.That(inclusive.Matches("2"), Is.True);
                Assert.That(inclusive.Matches("10"), Is.True);
                Assert.That(exclusive.Matches("2"), Is.False);
                Assert.That(exclusive.Matches("10"), Is.False);
                Assert.That(inclusive.Matches("9.5"), Is.True);
                // Ordinal text comparison would put "10" below "9" and pass a value outside the bound.
                Assert.That(inclusive.Matches("100"), Is.False);
            });
        }

        [Test]
        public void A_non_numeric_value_fails_a_bound_rather_than_being_compared_as_text()
        {
            ValueRestriction restriction =
                ValueRestriction.Create(minimum: new ValueRestriction.Bound(3m, true));

            Assert.That(restriction.Matches("abc"), Is.False);
        }

        [Test]
        public void A_decimal_is_read_invariantly()
        {
            // On a German or Russian machine the current culture reads 1.5 as 15. A checker whose
            // verdict depends on the operator's regional settings is not a checker.
            ValueRestriction restriction =
                ValueRestriction.Create(maximum: new ValueRestriction.Bound(2m, true));

            Assert.That(restriction.Matches("1.5"), Is.True);
        }

        [Test]
        public void Several_constraints_are_all_required()
        {
            ValueRestriction restriction = ValueRestriction.Create(
                pattern: "[A-Z]+-[0-9]+", minLength: 6);

            Assert.Multiple(() =>
            {
                Assert.That(restriction.Matches("AB-1234"), Is.True);
                Assert.That(restriction.Matches("A-1"), Is.False, "matches the pattern but is too short");
                Assert.That(restriction.Matches("abcdefgh"), Is.False, "long enough but wrong shape");
            });
        }

        [Test]
        public void A_simple_value_is_an_exact_comparison()
        {
            ValueRestriction restriction = ValueRestriction.Exact("IFCWALL");

            Assert.Multiple(() =>
            {
                Assert.That(restriction.Matches("IFCWALL"), Is.True);
                Assert.That(restriction.Matches("IFCWALLSTANDARDCASE"), Is.False);
                Assert.That(restriction.Matches("ifcwall"), Is.False);
            });
        }
    }
}
