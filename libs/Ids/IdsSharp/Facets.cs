using System.Collections.Generic;

namespace IdsSharp
{
    /// <summary>Whether a requirement facet must hold, must not hold, or is informational.</summary>
    public enum Cardinality
    {
        Required,
        Optional,
        Prohibited,
    }

    /// <summary>
    /// One condition on an entity. The same facet types appear on both sides of a specification: in
    /// <c>applicability</c> they SELECT which entities the specification is about, and in
    /// <c>requirements</c> they say what must then be true of them.
    /// </summary>
    public abstract class Facet
    {
        protected Facet(Cardinality cardinality, string? instructions)
        {
            Cardinality = cardinality;
            Instructions = instructions;
        }

        /// <summary>Meaningful in requirements; applicability facets are always "required" by nature.</summary>
        public Cardinality Cardinality { get; }

        /// <summary>Free text from the author, meant to be shown to whoever has to fix the finding.</summary>
        public string? Instructions { get; }

        /// <summary>A short description of what this facet asks, for a report line.</summary>
        public abstract string Describe();

        /// <summary>
        /// False for facets this library does not evaluate.
        ///
        /// <para>Load-bearing. A facet nobody implemented must NOT be reported as satisfied: a
        /// specification that silently skips its material requirement and says "passed" is worse than
        /// one that fails to load, because the failure is invisible and gets believed. Unsupported
        /// facets are parsed, kept, and reported as <see cref="ResultKind.NotChecked"/>.</para>
        /// </summary>
        public virtual bool IsSupported => true;
    }

    /// <summary>The IFC class of the entity, optionally with a predefined type.</summary>
    public sealed class EntityFacet : Facet
    {
        public EntityFacet(
            ValueRestriction name,
            ValueRestriction? predefinedType = null,
            Cardinality cardinality = Cardinality.Required,
            string? instructions = null)
            : base(cardinality, instructions)
        {
            Name = name;
            PredefinedType = predefinedType;
        }

        /// <summary>IFC class, e.g. IFCWALL. IDS writes these upper-case.</summary>
        public ValueRestriction Name { get; }

        public ValueRestriction? PredefinedType { get; }

        public override string Describe() =>
            PredefinedType == null
                ? $"entity {Name.Describe()}"
                : $"entity {Name.Describe()} of predefined type {PredefinedType.Describe()}";
    }

    /// <summary>An attribute of the entity itself (Name, Description, Tag…), as opposed to a property.</summary>
    public sealed class AttributeFacet : Facet
    {
        public AttributeFacet(
            ValueRestriction name,
            ValueRestriction? value = null,
            Cardinality cardinality = Cardinality.Required,
            string? instructions = null)
            : base(cardinality, instructions)
        {
            Name = name;
            Value = value;
        }

        public ValueRestriction Name { get; }

        /// <summary>Null means the attribute only has to EXIST and be non-empty.</summary>
        public ValueRestriction? Value { get; }

        public override string Describe() =>
            Value == null
                ? $"attribute {Name.Describe()} present"
                : $"attribute {Name.Describe()} = {Value.Describe()}";
    }

    /// <summary>A property in a property set — the facet nearly every real specification is built from.</summary>
    public sealed class PropertyFacet : Facet
    {
        public PropertyFacet(
            ValueRestriction propertySet,
            ValueRestriction baseName,
            ValueRestriction? value = null,
            string? dataType = null,
            Cardinality cardinality = Cardinality.Required,
            string? instructions = null)
            : base(cardinality, instructions)
        {
            PropertySet = propertySet;
            BaseName = baseName;
            Value = value;
            DataType = dataType;
        }

        public ValueRestriction PropertySet { get; }

        public ValueRestriction BaseName { get; }

        /// <summary>Null means the property only has to EXIST and be non-empty.</summary>
        public ValueRestriction? Value { get; }

        /// <summary>IFC measure type, e.g. IFCLABEL. Carried but not enforced.</summary>
        public string? DataType { get; }

        public override string Describe() =>
            Value == null
                ? $"property {PropertySet.Describe()}.{BaseName.Describe()} present"
                : $"property {PropertySet.Describe()}.{BaseName.Describe()} = {Value.Describe()}";
    }

    /// <summary>
    /// A facet this version parses but does not evaluate — <c>classification</c>, <c>material</c>,
    /// <c>partOf</c>.
    ///
    /// <para>Kept rather than dropped so a specification containing one still loads, and so the result
    /// can say WHICH requirement went unchecked. Claiming support that is not there is the failure this
    /// whole library is meant to avoid on someone else's behalf.</para>
    /// </summary>
    public sealed class UnsupportedFacet : Facet
    {
        public UnsupportedFacet(string elementName, Cardinality cardinality, string? instructions)
            : base(cardinality, instructions)
        {
            ElementName = elementName;
        }

        /// <summary>The IDS element name, e.g. "material".</summary>
        public string ElementName { get; }

        public override bool IsSupported => false;

        public override string Describe() => $"{ElementName} (not supported by this version)";
    }

    /// <summary>
    /// One rule: which entities it is about, and what must be true of them.
    /// </summary>
    public sealed class Specification
    {
        public Specification(
            string name,
            IReadOnlyList<Facet> applicability,
            IReadOnlyList<Facet> requirements,
            string? description = null,
            string? instructions = null,
            string? identifier = null,
            IReadOnlyList<string>? ifcVersions = null,
            int minOccurs = 0,
            int? maxOccurs = null)
        {
            Name = name;
            Applicability = applicability;
            Requirements = requirements;
            Description = description;
            Instructions = instructions;
            Identifier = identifier;
            IfcVersions = ifcVersions ?? new string[0];
            MinOccurs = minOccurs;
            MaxOccurs = maxOccurs;
        }

        public string Name { get; }
        public string? Description { get; }
        public string? Instructions { get; }
        public string? Identifier { get; }
        public IReadOnlyList<string> IfcVersions { get; }

        /// <summary>All of these must match for an entity to be subject to the requirements.</summary>
        public IReadOnlyList<Facet> Applicability { get; }

        public IReadOnlyList<Facet> Requirements { get; }

        /// <summary>How many entities must be applicable. 1 or more turns the specification into a
        /// "there must be at least one of these" check, which is a real and common use.</summary>
        public int MinOccurs { get; }

        /// <summary>Null means unbounded.</summary>
        public int? MaxOccurs { get; }
    }

    /// <summary>A parsed .ids file.</summary>
    public sealed class IdsDocument
    {
        public IdsDocument(
            IReadOnlyList<Specification> specifications,
            string? title = null,
            string? author = null,
            string? version = null,
            string? date = null,
            string? purpose = null)
        {
            Specifications = specifications;
            Title = title;
            Author = author;
            Version = version;
            Date = date;
            Purpose = purpose;
        }

        public IReadOnlyList<Specification> Specifications { get; }
        public string? Title { get; }
        public string? Author { get; }
        public string? Version { get; }
        public string? Date { get; }
        public string? Purpose { get; }
    }
}
