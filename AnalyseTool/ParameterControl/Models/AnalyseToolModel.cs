using Autodesk.Revit.UI;
using Binding = Autodesk.Revit.DB.Binding;

namespace AnalyseTool.ParameterControl.Models
{
    public class AnalyseToolModel : IAnalyseToolModel
    {
        private UIApplication app;
        private UIDocument uidoc;
        private Document doc;

        public AnalyseToolModel()
        {
            this.app = Context.UiApplication;
            uidoc = app.ActiveUIDocument;
            this.doc = app.ActiveUIDocument.Document;
        }

        public List<ParameterDefinition> AnalyzeData()
        {
            IList<KeyValuePair<string, Category>> allParamsCategoriesInProject = GetAllParametersInProject(doc);
            List<DataElement> dataElements = CreateDataElements(allParamsCategoriesInProject);
            IEnumerable<IGrouping<string, DataElement>> groupedByCategory = dataElements.GroupBy(x => x.Category.Name);
            List<ParameterDefinition> parameterDefinitionList = new List<ParameterDefinition>();

            // Iterate over each group
            foreach (IGrouping<string, DataElement> categoryGroup in groupedByCategory)
            {
                // Group the elements within each category by parameter name
                IEnumerable<IGrouping<string, DataElement>> groupedByParameterName = categoryGroup.GroupBy(x => x.ParameterName);

                foreach (IGrouping<string, DataElement> parameterGroup in groupedByParameterName)
                {
                    int parameterCount = parameterGroup.Count();
                    int parameterEmpty = parameterGroup.Count(x => string.IsNullOrEmpty(x.ParameterValue));
                    int parameterFilled = parameterCount - parameterEmpty;

                    // list of empty elements
                    List<ElementId> emptyElements = parameterGroup.Where(x => string.IsNullOrEmpty(x.ParameterValue)).Select(x => x.Element.Id).ToList();

                    // list if filled elements
                    List<ElementId> filledElements = parameterGroup.Where(x => !string.IsNullOrEmpty(x.ParameterValue)).Select(x => x.Element.Id).ToList();

                    // Assuming ParameterDefinition has a constructor that accepts these parameters
                    ParameterDefinition parameterDefinition = new ParameterDefinition(
                                                    parameterGroup.Key,
                                                    categoryGroup.Key,
                                                    parameterCount,
                                                    parameterFilled,
                                                    parameterEmpty,
                                                    emptyElements,
                                                    filledElements);

                    IEnumerable<IGrouping<string, DataElement>> groupedByParameterValue = parameterGroup
                        .Where(x => !string.IsNullOrEmpty(x.ParameterValue)) // get filled parameter
                        .GroupBy(x => x.ParameterValue); // group by value parameter

                    foreach (IGrouping<string, DataElement> valueGroup in groupedByParameterValue)
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
                    parameterDefinitionList.Add(parameterDefinition);

                }
            }
            return parameterDefinitionList;
        }
        public void SelectElements(IList<ElementId> elementIds)
        {
            uidoc.Selection.SetElementIds(elementIds);
        }
        private List<DataElement> CreateDataElements(IList<KeyValuePair<string, Category>> CategoryParameterMap)
        {
            List<DataElement> dataElements = new List<DataElement>();

            List<KeyValuePair<string, IList<Element>>> ParameterElementListMap = getParameterElementListMap(CategoryParameterMap);

            foreach (KeyValuePair<string, IList<Element>> dict in ParameterElementListMap)
            {
                foreach (Element element in dict.Value)
                {
                    dataElements.Add(new DataElement(element, dict.Key));
                }
            }
            return dataElements;
        }
        private IList<KeyValuePair<string, Category>> GetAllParametersInProject(Document document)
        {
            // Access the BindingMap
            BindingMap bindingMap = document.ParameterBindings;
            if (bindingMap == null)
            {
                TaskDialog.Show("Error", "BindingMap is null.");
                return null;
            }

            DefinitionBindingMapIterator iter = bindingMap.ForwardIterator();
            iter.Reset();

            IList<KeyValuePair<string, Category>> CategoryParameterMap = new List<KeyValuePair<string, Category>>();
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
                categories = GetCategoriesFromBinding(binding);

                if (categories != null)
                {
                    // Process the parameter definitions
                    if (definition is ExternalDefinition externalDefinition)
                    {
                        foreach (Category category in categories)
                        {
                            CategoryParameterMap.Add(new KeyValuePair<string, Category>(definition.Name, category));
                        }
                    }
                    else if (definition is InternalDefinition internalDefinition)
                    {
                        foreach (Category category in categories)
                        {
                            CategoryParameterMap.Add(new KeyValuePair<string, Category>(definition.Name, category));
                        }
                    }
                }
            }
            return CategoryParameterMap;
        }
        private List<KeyValuePair<string, IList<Element>>> getParameterElementListMap(IList<KeyValuePair<string, Category>> CategoryParameterMap)
        {
            List<ElementId> AllCategoriesIds = new List<ElementId>();
            List<KeyValuePair<string, IList<Element>>> ParameterElementListMap = new List<KeyValuePair<string, IList<Element>>>();
            foreach (KeyValuePair<string, Category> categoryElements in CategoryParameterMap)
            {
                string ParameterName = categoryElements.Key;
                Category category = categoryElements.Value;

                if (!AllCategoriesIds.Contains(category.Id))
                {
                    IList<Element> elements = new FilteredElementCollector(doc).OfCategory(category.BuiltInCategory).WhereElementIsNotElementType().ToElements();
                    AllCategoriesIds.Add(category.Id);
                    ParameterElementListMap.Add(new KeyValuePair<string, IList<Element>>(ParameterName, elements));
                }
                else
                {
                    foreach (KeyValuePair<string, IList<Element>> item in ParameterElementListMap)
                    {
                        IEnumerable<Element> elementList = item.Value.Where(x => x.Category.Id == category.Id);
                        ParameterElementListMap.Add(new KeyValuePair<string, IList<Element>>(ParameterName, elementList.ToList()));
                        break;
                    }
                }
            }
            return ParameterElementListMap;
        }
        private List<Category> GetCategoriesFromBinding(Binding binding)
        {
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
            //delete duplicate
            categories.Distinct().ToList();

            return categories;
        }

    }
}