using AnalyseTool.Models;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;

namespace AnalyseTool.ViewModels
{
    public class AnalyseToolViewModel : ObservableObject
    {
        private ObservableCollection<ParameterDefinition> parameterDefinitions;
        public ObservableCollection<ParameterDefinition> ParameterDefinitions
        {
            get { return parameterDefinitions; }
            set => SetProperty(ref parameterDefinitions, value);
        }

        public AnalyseToolViewModel()
        {
            ParameterDefinitions = new ObservableCollection<ParameterDefinition>();
            GetAllParametersInProject();
        }
        private void GetAllParametersInProject()
        {
            // Ensure document is not null
            if (ProgramContex.doc == null)
            {
                TaskDialog.Show("Error", "Document is null.");
                return;
            }

            // Access the BindingMap
            BindingMap bindingMap = ProgramContex.doc.ParameterBindings;
            if (bindingMap == null)
            {
                TaskDialog.Show("Error", "BindingMap is null.");
                return;
            }

            DefinitionBindingMapIterator iter = bindingMap.ForwardIterator();
            iter.Reset();

            while (iter.MoveNext())
            {
                Definition definition = iter.Key;
                Binding binding = iter.Current as Binding;

                // Ensure definition and binding are not null
                if (definition == null)
                {
                    TaskDialog.Show("Error", "Definition is null.");
                    continue;
                }

                if (binding == null)
                {
                    TaskDialog.Show("Error", "Binding is null.");
                    continue;
                }

                // Process the parameter definitions
                if (definition is ExternalDefinition externalDefinition)
                {
                    ParameterDefinitions.Add(new ParameterDefinition(
                        externalDefinition.Name,
                        externalDefinition.GUID.ToString(),
                        BuiltInParameter.INVALID.ToString() // Assuming you want to set INVALID for external definitions
                    ));
                }
                else if (definition is InternalDefinition internalDefinition)
                {
                    // Check for INVALID BuiltInParameter
                    string builtInParameterValue = internalDefinition.BuiltInParameter == BuiltInParameter.INVALID
                        ? BuiltInParameter.INVALID.ToString()
                        : internalDefinition.BuiltInParameter.ToString();

                    ParameterDefinitions.Add(new ParameterDefinition(
                        internalDefinition.Name,
                        Guid.Empty.ToString(), // Internal definitions do not have GUIDs
                        builtInParameterValue
                    ));
                }
            }

        }
    }
}