using AnalyseTool.Extensions;
using Autodesk.Revit.DB;
using System.Text.Json.Serialization;

namespace AnalyseTool.RevitCommands.DataModel
{
    public sealed record ParameterData : IEquatable<ParameterData>
    {
        [JsonIgnore]
        public Parameter Parameter { get; private set; }
        public string Name { get; }
        public long Id { get; }
        public string Level { get; }
        public long ElementId { get; }
        public bool IsTypeParameter { get; }
        public ParameterOrgin Orgin { get; }
        public string Value { get; }
        public ParameterData(Parameter parameter, long elementId, string level, bool isTypeParameter)
        {
            ElementId = elementId;
            Parameter = parameter;
            Name = parameter.Definition.Name;
            Id = parameter.Id.Value();
            Orgin = parameter.GetParameterOrgin();
            Value = parameter.GetParameterValue();
            Level = level;
            IsTypeParameter = isTypeParameter;
        }
    }
}
