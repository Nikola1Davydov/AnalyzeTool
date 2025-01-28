using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using BenchmarkDotNet.Parameters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Data;
using Binding = Autodesk.Revit.DB.Binding;

namespace AnalyseTool.ParameterControl.Models
{
    public class AnalyseToolModel : IAnalyseToolModel
    {
        public void GetAllParametersInProject()
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
                if (definition == null || binding == null)
                {
                    TaskDialog.Show("Error", "Definition or binding is null.");
                    continue;
                }

                // Retrieve the categories
                List<Category> categories = new List<Category>();
                if (binding is InstanceBinding instanceBinding)
                {
                    foreach (Category category in instanceBinding.Categories)
                    {
                        categories.Add(category);
                    }

                }
                else if (binding is TypeBinding typeBinding)
                {
                    foreach (Category category in typeBinding.Categories)
                    {
                        categories.Add(category);
                    }
                }

                if (categories != null)
                {
                    categories.Distinct().ToList();

                    // Process the parameter definitions
                    if (definition is ExternalDefinition externalDefinition)
                    {
                        AddParameterCategories(externalDefinition.Name, categories);
                    }
                    else if (definition is InternalDefinition internalDefinition)
                    {
                        AddParameterCategories(internalDefinition.Name, categories);
                    }
                }
            }
            CreateDataElements();
            AnalyzeData();

        }
        private void AddParameterCategories(string parameterName, List<Category> categories)
        {
            foreach (Category category in categories)
            {
                CategoryParameterMap.Add(new KeyValuePair<string, Category>(parameterName, category));
            }
        }
        private void CreateDataElements()
        {
            List<ElementId> AllCategoriesIds = new List<ElementId>();
            IList<Element> elements = new List<Element>();
            List<Tuple<string, Category, IList<Element>>> CategoryElementMap = new List<Tuple<string, Category, IList<Element>>>();

            foreach (KeyValuePair<string, Category> categoryElements in CategoryParameterMap)
            {
                string ParameterName = categoryElements.Key;
                Category category = categoryElements.Value;

                if (!AllCategoriesIds.Contains(category.Id))
                {
                    elements = new FilteredElementCollector(ProgramContex.doc).OfCategory(category.BuiltInCategory).WhereElementIsNotElementType().ToElements();
                    AllCategoriesIds.Add(category.Id);
                    CategoryElementMap.Add(new Tuple<string, Category, IList<Element>>(ParameterName, category, elements));
                }
                else
                {
                    foreach (var item in CategoryElementMap)
                    {
                        if (item.Item2.Id == category.Id)
                        {
                            CategoryElementMap.Add(new Tuple<string, Category, IList<Element>>(ParameterName, category, item.Item3));
                            break;
                        }
                    }
                }
            }

            foreach (var tuple in CategoryElementMap)
            {
                foreach (Element element in tuple.Item3)
                {
                    dataElements.Add(new DataElement(element, tuple.Item1));
                }
            }
        }
        private void AnalyzeData()
        {
            var groupedByCategory = dataElements.GroupBy(x => x.Category);
            // Initialize CollectionView
            ParameterCollectionView = CollectionViewSource.GetDefaultView(parameterDefinitions);
            ParameterCollectionView.Filter = FilterParameter;
            ParameterCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterDefinition.CategoriesString)));
            ParameterCollectionView.SortDescriptions.Add(new SortDescription(nameof(ParameterDefinition.CategoriesString), ListSortDirection.Ascending));

            //ParameterCollectionView.Filter = FilterParameter;
            // Iterate over each group
            foreach (var categoryGroup in groupedByCategory)
            {
                // Group the elements within each category by parameter name
                var groupedByParameterName = categoryGroup.GroupBy(x => x.ParameterName);

                foreach (var parameterGroup in groupedByParameterName)
                {
                    int parameterCount = parameterGroup.Count();
                    int parameterEmpty = parameterGroup.Count(x => string.IsNullOrEmpty(x.ParameterValue));
                    int parameterFilled = parameterCount - parameterEmpty;

                    // list of empty elements
                    List<ElementId> emptyElements = parameterGroup.Where(x => string.IsNullOrEmpty(x.ParameterValue)).Select(x => x.Element.Id).ToList();

                    // list if filled elements
                    List<ElementId> filledElements = parameterGroup.Where(x => !string.IsNullOrEmpty(x.ParameterValue)).Select(x => x.Element.Id).ToList();

                    // Assuming ParameterDefinition has a constructor that accepts these parameters
                    var parameterDefinition = new ParameterDefinition(
                                                                parameterGroup.Key,
                                                                categoryGroup.Key,
                                                                parameterCount,
                                                                parameterFilled,
                                                                parameterEmpty,
                                                                emptyElements,
                                                                filledElements);

                    var groupedByParameterValue = parameterGroup
                        .Where(x => !string.IsNullOrEmpty(x.ParameterValue)) // get filled parameter
                        .GroupBy(x => x.ParameterValue); // group by value parameter

                    foreach (var valueGroup in groupedByParameterValue)
                    {
                        int childParameterCount = valueGroup.Count();
                        int childParameterEmpty = 0;
                        int childParameterFilled = childParameterCount;

                        // Список пустых элементов для дочерних параметров
                        List<ElementId> childEmptyElements = new List<ElementId>();

                        // Список заполненных элементов для дочерних параметров
                        List<ElementId> childFilledElements = valueGroup.Where(x => !string.IsNullOrEmpty(x.ParameterValue)).Select(x => x.Element.Id).ToList();

                        // Создаем дочерний параметр (childDefinition) для каждого значения параметра
                        ParameterDefinition childDefinition = new ParameterDefinition(
                                                                valueGroup.Key, // Значение параметра становится ключом для дочернего элемента
                                                                categoryGroup.Key,
                                                                childParameterCount,
                                                                childParameterFilled,
                                                                childParameterEmpty,
                                                                childEmptyElements,
                                                                childFilledElements);

                        // Добавляем дочерний параметр в родительский
                        parameterDefinition.AddChild(childDefinition);
                        //FlattenParameterHierarchy(parameterDefinition);
                    }
                    parameterDefinitions.Add(parameterDefinition);

                }
            }
        }

    }
}
