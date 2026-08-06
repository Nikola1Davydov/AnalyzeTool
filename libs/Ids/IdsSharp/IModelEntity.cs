using System.Collections.Generic;

namespace IdsSharp
{
    /// <summary>
    /// The only thing this library knows about a model.
    ///
    /// <para>Small on purpose, and the small size is the design. A checker that took a Revit
    /// <c>Document</c> could only ever check Revit; one that took an IFC file could only check what had
    /// already been exported. Against this interface the same specifications answer both — the Revit
    /// model now, so a finding points at an element someone can fix immediately, and the exported IFC
    /// later, when the question is whether the delivery itself conforms.</para>
    ///
    /// <para>Note what is NOT here: no geometry, no relationships, no schema. IDS asks about identity,
    /// attributes and properties, so that is all an implementer has to supply.</para>
    ///
    /// <para><b>An implementation is an interpretation.</b> Mapping a Revit category to an IFC class,
    /// or a Revit parameter to a property-set property, is approximate and depends on export settings.
    /// An adapter that pretends otherwise makes this library report conformance it cannot know about,
    /// so an adapter should say plainly, wherever its results are shown, which model it read.</para>
    /// </summary>
    public interface IModelEntity
    {
        /// <summary>An opaque handle the caller can map back to whatever it came from — a Revit element
        /// id, an IFC GUID. The library never interprets it; it only carries it into results so a
        /// finding can be traced to the thing it is about.</summary>
        string Id { get; }

        /// <summary>A human label for reports: a name, a mark, a type name. Never used for matching.</summary>
        string Label { get; }

        /// <summary>IFC class, e.g. "IFCWALL". Upper-case, as IDS writes it.</summary>
        string IfcClass { get; }

        /// <summary>IFC predefined type, when the entity has one.</summary>
        string? PredefinedType { get; }

        /// <summary>
        /// An attribute of the entity itself — Name, Description, Tag.
        ///
        /// <para>Returns false when the entity has no such attribute. An attribute that exists but is
        /// empty must return true with an empty string: "absent" and "blank" are different findings,
        /// and only the implementation can tell them apart.</para>
        /// </summary>
        bool TryGetAttribute(string name, out string? value);

        /// <summary>
        /// A property of a property set. Same contract as <see cref="TryGetAttribute"/>: false means
        /// the property is not there at all.
        /// </summary>
        bool TryGetProperty(string propertySet, string name, out string? value);

        /// <summary>
        /// Property sets and their property names, so a facet whose property set or base name is a
        /// PATTERN rather than a literal can be answered.
        ///
        /// <para>A specification may ask for "any property matching <c>^Fire.*</c> in any Pset", which
        /// no lookup by exact name can serve. An implementation for which enumeration is expensive may
        /// return nothing; those facets are then reported as not checked, never as passed.</para>
        /// </summary>
        IEnumerable<KeyValuePair<string, string>> EnumerateProperties();

        /// <summary>Attribute names, for the same reason <see cref="EnumerateProperties"/> exists.</summary>
        IEnumerable<string> EnumerateAttributeNames();
    }
}
