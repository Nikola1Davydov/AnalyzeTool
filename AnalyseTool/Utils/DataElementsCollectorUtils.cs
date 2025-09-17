using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using Autodesk.Revit.DB;

namespace AnalyseTool.Utils
{
    internal class DataElementsCollectorUtils
    {
        public static IEnumerable<DataElement> GetAllElements(Document doc)
        {
            ElementFilter allModelCategoriesFilter = GetElementFilter(doc);

            FilteredElementCollector collectorInstances = new FilteredElementCollector(doc).WherePasses(allModelCategoriesFilter).WhereElementIsNotElementType();
            FilteredElementCollector collectorTypes = new FilteredElementCollector(doc).WherePasses(allModelCategoriesFilter).WhereElementIsElementType();
            FilteredElementCollector collector = collectorInstances.UnionWith(collectorTypes);

            IList<Element> elements = collector.ToElements();


            List<DataElement> result = new List<DataElement>(elements.Count);

            foreach (Element el in elements)
            {
                result.Add(new DataElement(el));
            }

            return result;
        }
        public static IEnumerable<DataElement> GetAllElementsByCategory(Document doc, string category)
        {
            List<Category> categories = GetModelCategories(doc);
            BuiltInCategory builtInCategory = categories.FirstOrDefault(x => x.Name.Equals(category)).BuiltInCategory;

            FilteredElementCollector collectorInstances = new FilteredElementCollector(doc).OfCategory(builtInCategory).WhereElementIsNotElementType();
            FilteredElementCollector collectorTypes = new FilteredElementCollector(doc).OfCategory(builtInCategory).WhereElementIsElementType();
            FilteredElementCollector collector = collectorInstances.UnionWith(collectorTypes);

            IList<Element> elements = collector.ToElements();


            List<DataElement> result = new List<DataElement>(elements.Count);

            foreach (Element el in elements)
            {
                result.Add(new DataElement(el));
            }

            return result;
        }
        private static ElementFilter GetElementFilter(Document doc)
        {
            List<ElementFilter> filters = new List<ElementFilter>();

            foreach (BuiltInCategory item in GetBuildInModelCategories(doc))
            {
                filters.Add(new ElementCategoryFilter(item));
            }
            return new LogicalOrFilter(filters);
        }

        public static List<BuiltInCategory> GetBuildInModelCategories(Document doc)
        {
            List<BuiltInCategory> modelBuiltInCategories = new List<BuiltInCategory>();

            List<Category> categories = GetModelCategories(doc);

            foreach (Category cat in categories)
            {
                if (cat != null && cat.CategoryType == CategoryType.Model)
                {
                    modelBuiltInCategories.Add(cat.BuiltInCategory);
                }
            }
            return modelBuiltInCategories;
        }
        public static List<Category> GetModelCategories(Document doc)
        {
            Categories categories = doc.Settings.Categories;

            List<Category> result  = categories.Cast<Category>()
                    .Where(cat => cat != null && cat.CategoryType == CategoryType.Model)
                    .ToList();

            return result;
        }
        public static List<string> GetModelCategoriesNames(Document doc)
        {
            List<Category> categories = GetModelCategories(doc);
            List<string> result = new List<string>();

            foreach (Category cat in categories)
            {
                result.Add(cat.Name);
            }
            List<string> ordered = result.OrderDescending().ToList();
            return ordered;
        }
    }
}
