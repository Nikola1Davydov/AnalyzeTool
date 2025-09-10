using AnalyseTool.ParameterControl.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalyseTool.ParameterControl
{
    internal class ParameterDefinitionRepository : IParameterDefinitionRepository
    {
        private List<ParameterDefinition> _parameterDefinitions = new List<ParameterDefinition>();
        public ParameterDefinitionRepository() { }

        public void Add(ParameterDefinition parameterDefinition)
        {
            _parameterDefinitions.Add(parameterDefinition);
        }
        public IEnumerable<ParameterDefinition> GetAll()
        {
            return _parameterDefinitions;
        }
        public void Clear()
        {
            _parameterDefinitions.Clear();
        }
        public bool Remove(ParameterDefinition parameterDefinition)
        {
            return _parameterDefinitions.Remove(parameterDefinition);
        }
    }
}
