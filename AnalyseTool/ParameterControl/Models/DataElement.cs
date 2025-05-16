namespace AnalyseTool.ParameterControl.Models
{
    public class DataElement
    {
        public Element Element { get; set; }
        public string Name { get; set; }
        public Category Category { get; set; }
        public string ParameterName { get; set; }
        public Parameter Parameter { get; set; }
        public string ParameterValue { get; set; }
        public Type ParameterType { get; set; }
        public DataElement(Element Element, string ParameterName)
        {
            this.Element = Element;
            this.Category = Element.Category;
            this.ParameterName = ParameterName;
            this.Parameter = Element.LookupParameter(ParameterName);
            if (Parameter != null)
            {
                this.ParameterValue = Parameter.AsValueString();
                this.ParameterType = Parameter.GetType();
            }
        }
    }
}
