using AnalyseTool.Extensions;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public sealed record ParameterData : IEquatable<ParameterData>
    {
        public Parameter Parameter { get; private set; }
        public string Name { get; }
        public long Id { get; }
        public long ElementId { get; }
        public ParameterOrgin Orgin { get; }
        public string Value { get; }
        public ParameterData(Parameter parameter, long elementId)
        {
            ElementId = elementId;
            Parameter = parameter;
            Name = parameter.Definition.Name;
            Id = parameter.Id.Value();
            Orgin = parameter.GetParameterOrgin();
            Value = parameter.GetParameterValue();
        }
    }
}
