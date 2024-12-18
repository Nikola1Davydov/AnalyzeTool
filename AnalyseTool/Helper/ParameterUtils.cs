using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalyseTool.Helper
{
    public static class ParameterUtils
    {
        public static string GetParameterValue(Parameter parameter)
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
        public static void getAllElementsParameters(Document document)
        {
            var allCategories = document.Settings.Categories.Cast<Category>().ToList();

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
            var allElements = new FilteredElementCollector(document).WhereElementIsNotElementType().WherePasses(elementMulticategoryFilter).ToList();

            foreach (var element in allElements)
            {
                var allParameters = element.Parameters;
                foreach (Parameter parameter in allParameters)
                {
                    // Получаем значение параметра
                    string parameterValue = Helper.ParameterUtils.GetParameterValue(parameter);

                    //// Добавляем элемент в dataElements
                    //dataElements.Add(new DataElement(element, element.Category, parameter.Definition.Name)
                    //{
                    //    Parameter = parameter,
                    //    ParameterValue = parameterValue,
                    //    ParameterType = parameter.GetType(),
                    //});
                }
            }
        }
    }
}
