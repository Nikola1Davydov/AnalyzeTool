using AnalyseTool.Extensions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
//using System.Text.Json.Serialization;

namespace AnalyseTool.RevitCommands.Model
{
    public sealed record ParameterData : IEquatable<ParameterData>
    {
        [JsonIgnore]
        public Parameter Parameter { get; private set; }
        [JsonProperty("name")]
        public string Name { get; init; }
        [JsonProperty("id")]
        public long Id { get; init; }
        [JsonProperty("level")]
        public string Level { get; init; }
        [JsonProperty("elementId")]
        public long ElementId { get; init; }
        [JsonProperty("isTypeParameter")]
        public bool IsTypeParameter { get; init; }
        [JsonProperty("orgin")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ParameterOrgin Orgin { get; init; }
        [JsonProperty("value")]
        public string Value { get; init; }
        [JsonProperty("storageType")]
        public string StorageType { get; init; }
        [JsonProperty("isReadOnly")]
        public bool IsReadOnly { get; init; }
        public ParameterData() { }
        public ParameterData(Parameter parameter, long elementId, string level, bool isTypeParameter)
        {
            ElementId = elementId;
            Parameter = parameter;
            Name = parameter.Definition.Name;
            Id = parameter.Id.Value;
            Orgin = parameter.GetParameterOrgin();
            Value = parameter.GetParameterValue();
            Level = level;
            StorageType = parameter.StorageType.ToString();
            IsReadOnly = parameter.IsReadOnly;

            IsTypeParameter = isTypeParameter;
        }
    }
}
