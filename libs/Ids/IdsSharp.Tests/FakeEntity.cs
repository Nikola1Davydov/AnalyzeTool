using System.Collections.Generic;
using System.Linq;
using IdsSharp;

namespace IdsSharp.Tests
{
    /// <summary>
    /// A model in a dictionary. That this is enough to exercise the whole evaluator is the point of
    /// <see cref="IModelEntity"/> being as small as it is — the checking logic can be tested without
    /// Revit, without IFC and without a file on disk.
    /// </summary>
    internal sealed class FakeEntity : IModelEntity
    {
        private readonly Dictionary<string, string?> _attributes = new Dictionary<string, string?>();
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();
        private readonly bool _enumerable;

        public FakeEntity(
            string ifcClass, string id = "1", string label = "element",
            string? predefinedType = null, bool enumerable = true)
        {
            IfcClass = ifcClass;
            Id = id;
            Label = label;
            PredefinedType = predefinedType;
            _enumerable = enumerable;
        }

        public string Id { get; }
        public string Label { get; }
        public string IfcClass { get; }
        public string? PredefinedType { get; }

        public FakeEntity WithAttribute(string name, string? value)
        {
            _attributes[name] = value;
            return this;
        }

        public FakeEntity WithProperty(string set, string name, string value)
        {
            _properties[set + "." + name] = value;
            return this;
        }

        public bool TryGetAttribute(string name, out string? value) =>
            _attributes.TryGetValue(name, out value);

        public bool TryGetProperty(string propertySet, string name, out string? value)
        {
            bool found = _properties.TryGetValue(propertySet + "." + name, out string? text);
            value = text;
            return found;
        }

        /// <summary>Empty when the double was built as non-enumerating — the case an implementation over
        /// an expensive model would hit, and the one that must produce NotChecked rather than a pass.</summary>
        public IEnumerable<KeyValuePair<string, string>> EnumerateProperties() =>
            _enumerable ? _properties : Enumerable.Empty<KeyValuePair<string, string>>();

        public IEnumerable<string> EnumerateAttributeNames() =>
            _enumerable ? _attributes.Keys : Enumerable.Empty<string>();
    }
}
