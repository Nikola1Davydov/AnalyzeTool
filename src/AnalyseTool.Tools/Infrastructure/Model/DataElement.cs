using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Infrastructure.Model
{
    public sealed record DataElement : IEquatable<DataElement>
    {
        [JsonIgnore]
        public Element Element { get; private set; }
        [JsonProperty("name")]
        public string Name { get; }
        [JsonProperty("id")]
        public long Id { get; }
        [JsonProperty("level")]
        public string Level { get; }
        [JsonProperty("isElementType")]
        public bool IsElementType { get; }
        [JsonProperty("elementTypeId")]
        public long ElementTypeId { get; }
        [JsonProperty("categoryName")]
        public string CategoryName { get; }
        [JsonProperty("parameters")]
        public List<ParameterData> Parameters { get; } = new();
        public DataElement(Element Element)
        {
            this.Element = Element;
            CategoryName = Element.Category?.Name;
            Name = Element.Name;
            Id = Element.Id.Value;
            Level = Element.Document.GetElement(Element.LevelId)?.Name ?? string.Empty;
            IsElementType = Element.GetTypeId() == ElementId.InvalidElementId;
            ElementTypeId = Element.GetTypeId().Value;
            Parameters = GetParameters(Element.Parameters).ToList();
        }
        private IEnumerable<ParameterData> GetParameters(ParameterSet parameterSet)
        {
            List<ParameterData> parameters = new List<ParameterData>();
            foreach (Parameter parameter in parameterSet)
            {
                ParameterData parameterData = new ParameterData(parameter, Id, Level, IsElementType);

                parameters.Add(parameterData);
            }
            return parameters;
        }
        public bool Equals(DataElement other)
        {
            if (other is null) return false;
            return Id == other.Id;
        }
        public override int GetHashCode() => Id.GetHashCode();

        public void Update()
        {
            Parameters.Clear();
            Parameters.AddRange(GetParameters(Element.Parameters));
        }
    }
}
