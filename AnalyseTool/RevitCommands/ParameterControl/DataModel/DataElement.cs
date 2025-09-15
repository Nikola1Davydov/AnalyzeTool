using AnalyseTool.Extensions;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public sealed record DataElement : IEquatable<DataElement>
    {
        public Element Element { get; private set; }
        public string Name { get; }
        public long Id { get; }
        public bool IsElementType { get; }
        public string CategoryName { get; }
        public List<ParameterData> Parameters { get; } = new();
        public DataElement(Element Element)
        {
            this.Element = Element;
            this.CategoryName = Element.Category?.Name;
            this.Name = Element.Name;
            Id = Element.Id.Value();
            IsElementType = Element is ElementType;
            Parameters = GetParameters(Element.Parameters).ToList();
        }
        private IEnumerable<ParameterData> GetParameters(ParameterSet parameterSet)
        {
            List<ParameterData> parameters = new List<ParameterData>();
            foreach (Parameter parameter in parameterSet)
            {
                parameters.Add(new ParameterData(parameter, Id));
            }
            return parameters;
        }
    }
}
