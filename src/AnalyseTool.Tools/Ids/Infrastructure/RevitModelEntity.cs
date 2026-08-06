using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using IdsSharp;
using System.Collections.Generic;

namespace AnalyseTool.Tools.Ids
{
    /// <summary>
    /// A Revit element seen through the six methods IdsSharp asks for.
    ///
    /// <para>Everything approximate about checking IDS against an authoring model is concentrated in
    /// this file and in <see cref="IfcClassMap"/>. That is the point of the interface being as small
    /// as it is: the checking logic upstream is exact, and the interpretation is here, where it can be
    /// read, argued with and corrected.</para>
    ///
    /// <para>The interpretation, stated plainly:</para>
    /// <list type="bullet">
    ///   <item><b>Class</b> comes from <c>IfcExportAs</c> if the element carries one, otherwise from the
    ///     category table.</item>
    ///   <item><b>A property</b> <c>Pset_WallCommon.FireRating</c> is looked for as a parameter named
    ///     <c>FireRating</c>, then as one named <c>Pset_WallCommon.FireRating</c>. Which Pset a
    ///     parameter really lands in is decided by the exporter's mapping tables, which are not read
    ///     here — so a match is evidence the value exists, not proof it will be exported into that
    ///     property set.</item>
    ///   <item><b>Enumeration returns nothing</b>, deliberately. See below.</item>
    /// </list>
    /// </summary>
    internal sealed class RevitModelEntity : IModelEntity
    {
        private readonly Element _element;
        private readonly Element? _type;

        public RevitModelEntity(Element element, IfcClassMap map)
        {
            _element = element;
            _type = element.Document.GetElement(element.GetTypeId());

            (string ifcClass, string? predefined) = map.Resolve(element);
            IfcClass = ifcClass;
            PredefinedType = predefined;
        }

        public string Id => _element.Id.Value.ToString();

        public string Label
        {
            get
            {
                string name = _element.Name ?? string.Empty;
                string category = _element.Category?.Name ?? string.Empty;
                return category.Length == 0 ? name : $"{category}: {name}";
            }
        }

        public string IfcClass { get; }

        public string? PredefinedType { get; }

        /// <summary>
        /// IFC attributes, as far as a Revit element has them.
        ///
        /// <para><c>Name</c> is the element's name, which is the one attribute the mapping is not a
        /// guess about. Everything else is looked up as a parameter of the same name, which is what an
        /// office that cares about an IFC attribute normally does.</para>
        /// </summary>
        public bool TryGetAttribute(string name, out string? value)
        {
            if (string.Equals(name, "Name", System.StringComparison.OrdinalIgnoreCase))
            {
                value = _element.Name ?? string.Empty;
                return true;
            }

            return TryReadParameter(name, out value);
        }

        public bool TryGetProperty(string propertySet, string name, out string? value)
        {
            // The plain name is how a shared parameter destined for a Pset is almost always called.
            if (TryReadParameter(name, out value)) return true;

            // The qualified form, used by offices that keep several properties of the same name apart.
            return TryReadParameter($"{propertySet}.{name}", out value);
        }

        /// <summary>
        /// Nothing — on purpose, and this is the most important line in the adapter.
        ///
        /// <para>A facet may ask for "any property matching <c>^Fire.*</c> in any property set". To
        /// answer that from a Revit element we would have to know which property set each parameter
        /// ends up in, which is decided by the exporter's mapping tables and by settings this code does
        /// not read. Inventing a property-set name for every parameter would let such facets PASS or
        /// FAIL on a fiction.</para>
        ///
        /// <para>Returning nothing makes IdsSharp report those facets as <c>NotChecked</c> — which is
        /// the truth: this model was not asked, because it cannot be asked. A wrong answer would be
        /// indistinguishable from a right one in the report.</para>
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> EnumerateProperties() =>
            System.Array.Empty<KeyValuePair<string, string>>();

        /// <summary>Same reasoning as <see cref="EnumerateProperties"/>: the set of IFC attributes an
        /// element will have is the exporter's decision, not this document's.</summary>
        public IEnumerable<string> EnumerateAttributeNames() => System.Array.Empty<string>();

        /// <summary>Instance value, then the type's — the order Revit itself resolves in. Reading only
        /// the instance would report every type-driven value as missing.</summary>
        private bool TryReadParameter(string name, out string? value)
        {
            Parameter? parameter = _element.LookupParameter(name);
            if (parameter == null || !parameter.HasValue) parameter = _type?.LookupParameter(name);

            if (parameter == null)
            {
                value = null;
                return false;
            }

            // Present but unset is NOT absent: "the element has a FireRating parameter and it is blank"
            // is the finding a check is usually looking for, and collapsing the two would lose it.
            value = parameter.GetParameterValue() ?? string.Empty;
            return true;
        }
    }
}
