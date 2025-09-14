using AnalyseTool.Extensions;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public sealed record ParameterData
    {
        public Parameter Parameter { get; private set; }
        public string Name { get; }
        public ParameterOrgin Orgin { get; }
        public ParameterData(Parameter parameter)
        {
            Parameter = parameter;
        }
    }
}
