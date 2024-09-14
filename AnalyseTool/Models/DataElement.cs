using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalyseTool
{
    public class DataElement
    {
        public Element Element {  get; set; }
        public string Name { get; set; }
        public Category Category { get; set; }
        public string ParameterName { get; set; }
        public Parameter Parameter { get; set; }
        public string ParameterValue {  get; set; }
        public Type ParameterType { get; set; }
        public DataElement(Element Element, Category Category, string ParameterName) 
        { 
            this.Element = Element;
            this.Category = Category;
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
