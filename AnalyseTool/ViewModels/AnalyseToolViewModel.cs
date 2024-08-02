using AnalyseTool.Models;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Binding = Autodesk.Revit.DB.Binding;

namespace AnalyseTool.ViewModels
{
    public class AnalyseToolViewModel : ObservableObject
    {
        public ICollectionView ParameterCollectionView { get; set; }
        public ICommand SelectElementsCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand ExportToPdfCommand { get; }

        private ObservableCollection<ParameterDefinition> parameterDefinitions;
        public ObservableCollection<ParameterDefinition> ParameterDefinitions
        {
            get { return parameterDefinitions; }
            set => SetProperty(ref parameterDefinitions, value);
        }
        private string _parameterFilter = string.Empty;
        public string ParameterFilter
        {
            get => _parameterFilter;
            set
            {
                _parameterFilter = value;
                OnPropertyChanged(nameof(ParameterFilter));
                ParameterCollectionView.Refresh();
            }
        }
        List<DataElement> dataElements;
        IList<KeyValuePair<string, Category>> CategoryParameterMap;

        public AnalyseToolViewModel()
        {
            ParameterDefinitions = new ObservableCollection<ParameterDefinition>();
            CategoryParameterMap = new List<KeyValuePair<string, Category>>();
            dataElements = new List<DataElement> { };
            GetAllParametersInProject();
            SelectElementsCommand = new RelayCommand<ParameterDefinition>(SelectElements);
            ExportToExcelCommand = new RelayCommand(ExportCSV.ExportToExcel);
            ExportToPdfCommand = new RelayCommand(ExportPDF.ExportToPdf);
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

            //getAllElementsParameters();
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
                    dataElements.Add(new DataElement(element, tuple.Item2, tuple.Item1));
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
                    var emptyElements = parameterGroup.Where(x => string.IsNullOrEmpty(x.ParameterValue)).Select(x => x.Element.Id).ToList();

                    // Assuming ParameterDefinition has a constructor that accepts these parameters
                    var parameterDefinition = new ParameterDefinition(
                                                                parameterGroup.Key,
                                                                categoryGroup.Key,
                                                                parameterCount,
                                                                parameterFilled,
                                                                parameterEmpty,
                                                                emptyElements);

                    parameterDefinitions.Add(parameterDefinition);
                }
            }
        }
        private bool FilterParameter(object obj)
        {
            if (obj is ParameterDefinition parameterDefinition)
            {
                return parameterDefinition.Categories.Name.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase) ||
                    parameterDefinition.Name.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase);
            }
            return false;
        }

        private void SelectElements(ParameterDefinition parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.Elements != null)
            {
                ProgramContex.uidoc.Selection.SetElementIds(parameterDefinition.Elements);
            }
        }
        private void getAllElementsParameters()
        {
            var allCategories = ProgramContex.doc.Settings.Categories.Cast<Category>().ToList();

            // Создаем коллекцию всех встроенных категорий
            ICollection<BuiltInCategory> allBuiltInCategories = new Collection<BuiltInCategory>(allCategories.Select(x =>
            {
                try
                {
                    return (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), x.Id.IntegerValue.ToString());
                }
                catch
                {
                    return BuiltInCategory.INVALID;
                }
            }).Where(c => c != BuiltInCategory.INVALID).ToList());

            // Создаем фильтр для всех категорий
            ElementMulticategoryFilter elementMulticategoryFilter = new ElementMulticategoryFilter(allBuiltInCategories);

            // Получаем все элементы, которые не являются типами элементов и проходят фильтр категорий
            var allElements = new FilteredElementCollector(ProgramContex.doc).WhereElementIsNotElementType().WherePasses(elementMulticategoryFilter).ToList();

            foreach (var element in allElements)
            {
                var allParameters = element.Parameters;
                foreach (Parameter parameter in allParameters)
                {
                    // Получаем значение параметра
                    string parameterValue = GetParameterValue(parameter);

                    // Добавляем элемент в dataElements
                    dataElements.Add(new DataElement(element, element.Category, parameter.Definition.Name)
                    {
                        Parameter = parameter,
                        ParameterValue = parameterValue,
                        ParameterType = parameter.GetType(),
                    });
                }

            }
        }
        private string GetParameterValue(Parameter parameter)
        {
            // Получаем значение параметра в зависимости от его типа
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    return parameter.AsDouble().ToString();
                case StorageType.Integer:
                    return parameter.AsInteger().ToString();
                case StorageType.String:
                    return parameter.AsString();
                case StorageType.ElementId:
                    return parameter.AsElementId().IntegerValue.ToString();
                default:
                    return string.Empty;
            }
        }


    }
}