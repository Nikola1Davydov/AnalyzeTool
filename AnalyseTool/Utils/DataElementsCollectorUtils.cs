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

            IList<Element> elements = collector.ToElements().ToList();

            return elements.Select(x => new DataElement(x));
        }

        private static ElementFilter GetElementFilter(Document doc)
        {
            List<ElementFilter> filters = new List<ElementFilter>();

            foreach (BuiltInCategory item in GetModelCategories(doc))
            {
                filters.Add(new ElementCategoryFilter(item));
            }
            return new LogicalOrFilter(filters);
        }

        public static List<BuiltInCategory> GetModelCategories(Document doc)
        {
            List<BuiltInCategory> modelBuiltInCategories = new List<BuiltInCategory>();
            Categories categories = doc.Settings.Categories;

            categories.Cast<Category>()
                    .Where(cat => cat != null && cat.CategoryType == CategoryType.Model)
                    .ToList();

            foreach (Category cat in categories)
            {
                if (cat != null && cat.CategoryType == CategoryType.Model)
                {
                    modelBuiltInCategories.Add(cat.BuiltInCategory);
                }
            }
            return modelBuiltInCategories;
        }
    }
}
