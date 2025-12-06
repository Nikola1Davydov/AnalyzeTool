using AnalyseTool.Extensions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.Model
{
    public sealed record ParameterData : IEquatable<ParameterData>
    {
        [JsonIgnore]
        public Parameter Parameter { get; private set; }
        [JsonProperty("name")]
        public string Name { get; }
        [JsonProperty("id")]
        public long Id { get; }
        [JsonProperty("level")]
        public string Level { get; }
        [JsonProperty("elementId")]
        public long ElementId { get; }
        [JsonProperty("isTypeParameter")]
        public bool IsTypeParameter { get; }
        [JsonProperty("orgin")]
        public ParameterOrgin Orgin { get; }
        [JsonProperty("value")]
        public string Value { get; }
        public ParameterData(Parameter parameter, long elementId, string level, bool isTypeParameter)
        {
            ElementId = elementId;
            Parameter = parameter;
            Name = parameter.Definition.Name;
            Id = parameter.Id.Value;
            Orgin = parameter.GetParameterOrgin();
            Value = parameter.GetParameterValue();
            Level = level;
            IsTypeParameter = isTypeParameter;
        }
    }
}
