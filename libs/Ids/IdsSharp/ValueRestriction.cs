using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace IdsSharp
{
    /// <summary>
    /// What a value has to be for a facet to match: a literal, or an XSD restriction.
    ///
    /// <para>This is where a checker is right or wrong. Every other part of IDS decides WHICH values
    /// to look at; this decides whether one passes, and a subtle mistake here reports a clean model
    /// or a broken one with equal confidence.</para>
    ///
    /// <para>An <b>absent</b> restriction is not the same as an empty one. Absent means "the facet only
    /// requires that this exists" — a property with no value constraint is satisfied by any value at
    /// all. That distinction is why <see cref="Matches"/> takes a nullable string and answers about
    /// existence separately from content.</para>
    /// </summary>
    public sealed class ValueRestriction
    {
        private readonly Regex? _pattern;

        private ValueRestriction(
            string? simpleValue,
            IReadOnlyList<string>? enumeration,
            string? pattern,
            Bound? minimum,
            Bound? maximum,
            int? minLength,
            int? maxLength)
        {
            SimpleValue = simpleValue;
            Enumeration = enumeration;
            Pattern = pattern;
            Minimum = minimum;
            Maximum = maximum;
            MinLength = minLength;
            MaxLength = maxLength;

            // Compiled once, at parse time, so a malformed pattern is a load-time error rather than a
            // per-element surprise — and so a specification run over 10 000 elements is not 10 000
            // regex compilations.
            if (pattern != null) _pattern = new Regex(pattern, RegexOptions.CultureInvariant);
        }

        /// <summary>An exact value. IDS writes this as the facet's plain text content.</summary>
        public string? SimpleValue { get; }

        /// <summary>xs:enumeration — the value must be one of these.</summary>
        public IReadOnlyList<string>? Enumeration { get; }

        /// <summary>xs:pattern — an XSD regular expression, anchored (XSD patterns always match whole).</summary>
        public string? Pattern { get; }

        /// <summary>xs:minInclusive / xs:minExclusive.</summary>
        public Bound? Minimum { get; }

        /// <summary>xs:maxInclusive / xs:maxExclusive.</summary>
        public Bound? Maximum { get; }

        /// <summary>xs:minLength, or xs:length written as both bounds.</summary>
        public int? MinLength { get; }

        /// <summary>xs:maxLength, or xs:length written as both bounds.</summary>
        public int? MaxLength { get; }

        /// <summary>A numeric bound, with whether the boundary value itself is allowed.</summary>
        public sealed class Bound
        {
            public Bound(decimal value, bool inclusive)
            {
                Value = value;
                Inclusive = inclusive;
            }

            public decimal Value { get; }
            public bool Inclusive { get; }
        }

        public static ValueRestriction Exact(string value) =>
            new ValueRestriction(value, null, null, null, null, null, null);

        public static ValueRestriction Create(
            IReadOnlyList<string>? enumeration = null,
            string? pattern = null,
            Bound? minimum = null,
            Bound? maximum = null,
            int? minLength = null,
            int? maxLength = null) =>
            new ValueRestriction(null, enumeration, pattern, minimum, maximum, minLength, maxLength);

        /// <summary>
        /// Whether <paramref name="value"/> satisfies every constraint this carries.
        ///
        /// <para>A null value never matches. "The property is absent" is a different answer from "the
        /// property is there and wrong", and collapsing them is how a report loses the finding people
        /// most want — an element with no value at all.</para>
        ///
        /// <para>Constraints are ANDed, which is what XSD means by several facets on one restriction.</para>
        /// </summary>
        public bool Matches(string? value)
        {
            if (value == null) return false;

            if (SimpleValue != null)
                return string.Equals(value, SimpleValue, StringComparison.Ordinal);

            if (Enumeration != null && !Enumeration.Contains(value, StringComparer.Ordinal))
                return false;

            // XSD patterns are implicitly anchored to the whole value; .NET's are not. Without this,
            // "^A" would accept "BA" and a naming standard would pass on names that violate it.
            if (_pattern != null)
            {
                Match match = _pattern.Match(value);
                if (!match.Success || match.Index != 0 || match.Length != value.Length) return false;
            }

            if (MinLength.HasValue && value.Length < MinLength.Value) return false;
            if (MaxLength.HasValue && value.Length > MaxLength.Value) return false;

            if (Minimum != null || Maximum != null)
            {
                // Bounds are numeric. A value that is not a number fails them rather than being compared
                // as text: "abc" is not "greater than 3", and an ordinal comparison would say it is.
                if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal number))
                    return false;

                if (Minimum != null &&
                    (Minimum.Inclusive ? number < Minimum.Value : number <= Minimum.Value)) return false;

                if (Maximum != null &&
                    (Maximum.Inclusive ? number > Maximum.Value : number >= Maximum.Value)) return false;
            }

            return true;
        }

        /// <summary>A short human description, for the line a person reads in a report.</summary>
        public string Describe()
        {
            if (SimpleValue != null) return $"\"{SimpleValue}\"";

            List<string> parts = new List<string>();
            if (Enumeration != null) parts.Add("one of " + string.Join(", ", Enumeration.Select(e => $"\"{e}\"")));
            if (Pattern != null) parts.Add($"matching {Pattern}");
            if (Minimum != null) parts.Add((Minimum.Inclusive ? "at least " : "greater than ") + Minimum.Value);
            if (Maximum != null) parts.Add((Maximum.Inclusive ? "at most " : "less than ") + Maximum.Value);
            if (MinLength.HasValue) parts.Add($"at least {MinLength} character(s)");
            if (MaxLength.HasValue) parts.Add($"at most {MaxLength} character(s)");

            return parts.Count == 0 ? "any value" : string.Join(" and ", parts);
        }

        public override string ToString() => Describe();
    }
}
