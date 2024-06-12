using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnalyseTool.Models
{
    public class ParameterDefinition : ObservableObject
    {
        private string name;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }
        private string guid;
        public string GUID
        {
            get => guid;
            set => SetProperty(ref guid, value);
        }
        private string builtInParameter;
        public string BuiltInParameter
        {
            get => builtInParameter;
            set => SetProperty(ref builtInParameter, value);
        }
        public ParameterDefinition(string Name, string GUID, string BuiltInParameter)
        {
            this.Name = Name;
            this.GUID = GUID;
            this.BuiltInParameter = BuiltInParameter;
        }
    }
}
