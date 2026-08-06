using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace IdsSharp
{
    /// <summary>Raised when a file is not a usable IDS document. Loud on purpose: a checker that
    /// silently loads half a specification reports a clean model it never checked.</summary>
    public sealed class IdsParseException : Exception
    {
        public IdsParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads a buildingSMART IDS file into <see cref="IdsDocument"/>.
    ///
    /// <para>XML only — <c>System.Xml.Linq</c>, no dependency. Namespace-tolerant on read (files in the
    /// wild carry several versions of the IDS namespace, and some carry none) but strict about
    /// structure: an element it does not recognise inside a facet list becomes an
    /// <see cref="UnsupportedFacet"/> rather than being dropped, so a specification can never quietly
    /// lose a requirement on the way in.</para>
    /// </summary>
    public static class IdsParser
    {
        private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

        /// <summary>
        /// Reads a .ids file from disk, auditing it first.
        ///
        /// <para>The audit is on by default because this is the entry point a real consumer uses, and
        /// the failure it prevents is the expensive one: a specification that is subtly invalid parses
        /// into something plausible, checks a model, and produces a report nobody can tell from a real
        /// one. <see cref="Parse(string)"/> stays lenient for fragments and for callers who have
        /// already audited.</para>
        /// </summary>
        /// <param name="path">Path to the .ids file.</param>
        /// <param name="audit">False to skip buildingSMART's auditor.</param>
        public static IdsDocument ParseFile(string path, bool audit = true)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No path.", nameof(path));
            if (!File.Exists(path)) throw new IdsParseException($"There is no IDS file at '{path}'.");

            if (audit)
            {
                IdsAuditResult result = IdsAudit.CheckFile(path);

                // Errors refuse; warnings do not. A file with structure warnings is still checkable, and
                // refusing it would make the audit something callers switch off — which would cost the
                // errors too.
                if (result.HasErrors)
                    throw new IdsParseException(
                        $"'{Path.GetFileName(path)}' is not valid IDS, so nothing checked against it " +
                        $"could be trusted. {result.Describe()}");
            }

            return Parse(File.ReadAllText(path));
        }

        public static IdsDocument Parse(string xml)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (Exception ex)
            {
                throw new IdsParseException($"This is not well-formed XML: {ex.Message}");
            }

            XElement root = document.Root
                ?? throw new IdsParseException("The document is empty.");

            if (root.Name.LocalName != "ids")
                throw new IdsParseException(
                    $"The root element is <{root.Name.LocalName}>, not <ids>. This is not an IDS file.");

            XElement? info = Child(root, "info");

            List<Specification> specifications =
                (Child(root, "specifications")?.Elements().Where(e => e.Name.LocalName == "specification")
                 ?? Enumerable.Empty<XElement>())
                .Select(ParseSpecification)
                .ToList();

            // Not an error — an IDS with no specifications is valid and checks nothing — but the caller
            // has to be able to tell that apart from "everything passed", so it is left to the result.
            return new IdsDocument(
                specifications,
                Value(info, "title"),
                Value(info, "author"),
                Value(info, "version"),
                Value(info, "date"),
                Value(info, "purpose"));
        }

        private static Specification ParseSpecification(XElement element)
        {
            string name = (string?)element.Attribute("name") ?? "(unnamed)";

            XElement? applicability = Child(element, "applicability");
            XElement? requirements = Child(element, "requirements");

            if (applicability == null)
                throw new IdsParseException(
                    $"Specification '{name}' has no <applicability>, so there is no way to know which " +
                    "entities it is about.");

            (int minOccurs, int? maxOccurs) = ParseOccurs(applicability);

            return new Specification(
                name,
                applicability.Elements().Select(ParseFacet).ToList(),
                requirements?.Elements().Select(ParseFacet).ToList() ?? new List<Facet>(),
                (string?)element.Attribute("description"),
                (string?)element.Attribute("instructions"),
                (string?)element.Attribute("identifier"),
                ((string?)element.Attribute("ifcVersion"))?.Split(' ')
                    .Where(v => v.Length > 0).ToList(),
                minOccurs,
                maxOccurs);
        }

        private static (int Min, int? Max) ParseOccurs(XElement applicability)
        {
            int min = 0;
            string? minText = (string?)applicability.Attribute("minOccurs");
            if (minText != null && int.TryParse(minText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                min = parsed;

            string? maxText = (string?)applicability.Attribute("maxOccurs");
            int? max = null;
            if (maxText != null && !string.Equals(maxText, "unbounded", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(maxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
                max = parsedMax;

            return (min, max);
        }

        private static Facet ParseFacet(XElement element)
        {
            Cardinality cardinality = ParseCardinality((string?)element.Attribute("cardinality"));
            string? instructions = (string?)element.Attribute("instructions");

            switch (element.Name.LocalName)
            {
                case "entity":
                    return new EntityFacet(
                        RequiredRestriction(element, "name", element.Name.LocalName),
                        Restriction(Child(element, "predefinedType")),
                        cardinality, instructions);

                case "attribute":
                    return new AttributeFacet(
                        RequiredRestriction(element, "name", element.Name.LocalName),
                        Restriction(Child(element, "value")),
                        cardinality, instructions);

                case "property":
                    return new PropertyFacet(
                        RequiredRestriction(element, "propertySet", element.Name.LocalName),
                        RequiredRestriction(element, "baseName", element.Name.LocalName),
                        Restriction(Child(element, "value")),
                        (string?)element.Attribute("dataType"),
                        cardinality, instructions);

                default:
                    // classification, material, partOf — and anything a later IDS version adds. Kept so
                    // the result can name what went unchecked instead of implying it passed.
                    return new UnsupportedFacet(element.Name.LocalName, cardinality, instructions);
            }
        }

        private static Cardinality ParseCardinality(string? text)
        {
            if (string.Equals(text, "prohibited", StringComparison.OrdinalIgnoreCase)) return Cardinality.Prohibited;
            if (string.Equals(text, "optional", StringComparison.OrdinalIgnoreCase)) return Cardinality.Optional;
            return Cardinality.Required;
        }

        private static ValueRestriction RequiredRestriction(XElement parent, string childName, string facetName)
        {
            ValueRestriction? restriction = Restriction(Child(parent, childName));
            if (restriction == null)
                throw new IdsParseException(
                    $"A <{facetName}> facet has no <{childName}>, which it cannot do without.");

            return restriction;
        }

        /// <summary>
        /// A facet's value holder: either plain text (a literal) or an <c>xs:restriction</c>.
        /// </summary>
        private static ValueRestriction? Restriction(XElement? holder)
        {
            if (holder == null) return null;

            XElement? restriction = holder.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "restriction");

            if (restriction == null)
            {
                XElement? simple = holder.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "simpleValue");

                string text = (simple ?? holder).Value.Trim();
                return text.Length == 0 ? null : ValueRestriction.Exact(text);
            }

            List<string> enumeration = restriction.Elements()
                .Where(e => e.Name.LocalName == "enumeration")
                .Select(e => (string?)e.Attribute("value") ?? string.Empty)
                .ToList();

            string? pattern = restriction.Elements()
                .Where(e => e.Name.LocalName == "pattern")
                .Select(e => (string?)e.Attribute("value"))
                .FirstOrDefault(v => v != null);

            ValueRestriction.Bound? minimum =
                Bound(restriction, "minInclusive", true) ?? Bound(restriction, "minExclusive", false);
            ValueRestriction.Bound? maximum =
                Bound(restriction, "maxInclusive", true) ?? Bound(restriction, "maxExclusive", false);

            int? length = Length(restriction, "length");
            int? minLength = Length(restriction, "minLength") ?? length;
            int? maxLength = Length(restriction, "maxLength") ?? length;

            try
            {
                return ValueRestriction.Create(
                    enumeration.Count > 0 ? enumeration : null,
                    pattern, minimum, maximum, minLength, maxLength);
            }
            catch (ArgumentException ex)
            {
                // A bad pattern would otherwise match nothing, which looks exactly like a cautious rule
                // doing its job while being a rule that never ran.
                throw new IdsParseException($"The pattern '{pattern}' is not a valid expression: {ex.Message}");
            }
        }

        private static ValueRestriction.Bound? Bound(XElement restriction, string name, bool inclusive)
        {
            string? text = restriction.Elements()
                .Where(e => e.Name.LocalName == name)
                .Select(e => (string?)e.Attribute("value"))
                .FirstOrDefault(v => v != null);

            return text != null &&
                   decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
                ? new ValueRestriction.Bound(value, inclusive)
                : null;
        }

        private static int? Length(XElement restriction, string name)
        {
            string? text = restriction.Elements()
                .Where(e => e.Name.LocalName == name)
                .Select(e => (string?)e.Attribute("value"))
                .FirstOrDefault(v => v != null);

            return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : (int?)null;
        }

        /// <summary>Namespace-agnostic child lookup: files in the wild carry several versions of the IDS
        /// namespace and some carry none, and refusing them would be strictness that helps nobody.</summary>
        private static XElement? Child(XElement? parent, string localName) =>
            parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

        private static string? Value(XElement? parent, string localName)
        {
            string? text = Child(parent, localName)?.Value.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
