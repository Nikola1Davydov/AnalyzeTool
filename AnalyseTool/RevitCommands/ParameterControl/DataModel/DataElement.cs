using Autodesk.Revit.DB;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    public sealed record DataElement
    {
        public Element Element { get; private set; }
        public string Name { get; }
        public string Category { get; }
        public List<ParameterData> Parameters { get; } = new();
        public DataElement(Element Element)
        {
            this.Element = Element;
            this.Category = Element.Category.Name;
        }
    }
}
