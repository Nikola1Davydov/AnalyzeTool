using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Elements
{
    public class DataElementsCollectorService
    {
        public IEnumerable<DataElement> GetAllElements(Document doc)
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
        public IEnumerable<DataElement> GetAllElementsByCategory(Document doc, string category)
        {
            List<Category> categories = GetModelCategories(doc);

            Category? match = categories.FirstOrDefault(x => x.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
            if (match == null) return new List<DataElement>();

            BuiltInCategory builtInCategory = match.BuiltInCategory;

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
        /// <summary>Lean, filterable element listing for AI/MCP callers. Returns element identity and only
        /// the requested parameters (token-friendly), with optional name filter and count cap.</summary>
        public IEnumerable<ElementSummary> GetElementSummaries(
            Document doc, string category,
            string? nameContains = null,
            IReadOnlyCollection<string>? parameterNames = null,
            int? limit = null)
        {
            Category? match = ResolveCategory(doc, category);
            if (match == null) return new List<ElementSummary>();

            BuiltInCategory bic = match.BuiltInCategory;
            FilteredElementCollector instances = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
            FilteredElementCollector types = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsElementType();
            IEnumerable<Element> elements = instances.UnionWith(types).ToElements();

            if (!string.IsNullOrWhiteSpace(nameContains))
                elements = elements.Where(e => (e.Name ?? string.Empty)
                    .IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            if (limit.HasValue && limit.Value > 0)
                elements = elements.Take(limit.Value);

            HashSet<string>? wanted = (parameterNames != null && parameterNames.Count > 0)
                ? new HashSet<string>(parameterNames, StringComparer.OrdinalIgnoreCase)
                : null;

            List<ElementSummary> result = new List<ElementSummary>();
            foreach (Element el in elements)
            {
                result.Add(new ElementSummary
                {
                    Id = el.Id.Value,
                    Name = el.Name,
                    Category = el.Category?.Name ?? string.Empty,
                    Level = doc.GetElement(el.LevelId)?.Name ?? string.Empty,
                    IsType = el.GetTypeId() == ElementId.InvalidElementId,
                    Parameters = wanted == null ? null : ExtractParameters(el, wanted)
                });
            }
            return result;
        }

        /// <summary>Discovery: parameter names available on a category, sampled from a representative
        /// element (instance + its type). Lets AI callers learn which parameterNames to request.</summary>
        public IEnumerable<CategoryParameterInfo> GetCategoryParameterInfos(Document doc, string category)
        {
            Category? match = ResolveCategory(doc, category);
            if (match == null) return new List<CategoryParameterInfo>();

            BuiltInCategory bic = match.BuiltInCategory;
            Element? sample = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().FirstElement()
                           ?? new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsElementType().FirstElement();
            if (sample == null) return new List<CategoryParameterInfo>();

            Dictionary<string, CategoryParameterInfo> map = new(StringComparer.OrdinalIgnoreCase);
            bool sampleIsType = sample.GetTypeId() == ElementId.InvalidElementId;
            AddParameterInfos(sample.Parameters, sampleIsType, map);

            // When the sample is an instance, also surface its type's parameters.
            if (!sampleIsType)
            {
                Element? type = doc.GetElement(sample.GetTypeId());
                if (type != null) AddParameterInfos(type.Parameters, isType: true, map);
            }

            return map.Values.OrderBy(p => p.Name).ToList();
        }

        private Category? ResolveCategory(Document doc, string category) =>
            GetModelCategories(doc).FirstOrDefault(x => x.Name.Equals(category, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// One row per (element, parameter) — the shape a validation pipeline needs.
        ///
        /// <para>The other projections answer "what elements are there". This answers "what values are
        /// there to check", which is a different granularity: a finding is about ONE parameter of ONE
        /// element, and so is a BCF topic. Flat, because a <c>Filter</c> condition names a field, and
        /// reaching into a nested list to find the parameter called "Mark" is something it cannot do.</para>
        ///
        /// <para>The row carries <c>elementId</c> and <c>id</c> (the PARAMETER id) under exactly those
        /// names, because that is the pair <c>SetDataToParameters</c> consumes. A corrected value can
        /// therefore be written back by binding these rows straight into it, with no glue node in
        /// between — which is what makes read → check → fix a pipeline rather than a plan.</para>
        ///
        /// <para>A parameter the element does not have still produces a row, with an empty value and
        /// <c>found: false</c>. "This element has no Mark at all" is the single most common finding a
        /// parameter check is looking for, and a missing row cannot be reported.</para>
        /// </summary>
        public IEnumerable<ParameterRow> GetParameterRows(
            Document doc, string category, IReadOnlyCollection<string> parameterNames,
            string? nameContains = null, int? limit = null)
        {
            Category? match = ResolveCategory(doc, category);
            if (match == null) return new List<ParameterRow>();

            BuiltInCategory bic = match.BuiltInCategory;
            IEnumerable<Element> elements = new FilteredElementCollector(doc)
                .OfCategory(bic).WhereElementIsNotElementType().ToElements();

            if (!string.IsNullOrWhiteSpace(nameContains))
                elements = elements.Where(e => (e.Name ?? string.Empty)
                    .IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            if (limit.HasValue && limit.Value > 0) elements = elements.Take(limit.Value);

            List<ParameterRow> rows = new();
            foreach (Element el in elements)
            {
                string level = doc.GetElement(el.LevelId)?.Name ?? string.Empty;
                Element? type = doc.GetElement(el.GetTypeId());

                foreach (string wanted in parameterNames)
                {
                    // Instance first, then the type: that is the order Revit itself resolves a value in,
                    // and a check that looked only at the instance would report every type-driven value
                    // as missing.
                    Parameter? parameter = el.LookupParameter(wanted) ?? type?.LookupParameter(wanted);
                    bool isTypeParameter = parameter is not null && el.LookupParameter(wanted) is null;

                    if (parameter is null)
                    {
                        rows.Add(new ParameterRow(
                            el.Id.Value, el.Name ?? string.Empty, el.Category?.Name ?? string.Empty, level,
                            0, wanted, string.Empty, string.Empty, true, false, false));
                        continue;
                    }

                    // Built through ParameterData so value extraction and origin stay in one place.
                    ParameterData data = new(parameter, el.Id.Value, level, isTypeParameter);
                    rows.Add(new ParameterRow(
                        data.ElementId, el.Name ?? string.Empty, el.Category?.Name ?? string.Empty, data.Level,
                        data.Id, data.Name, data.Value ?? string.Empty, data.StorageType,
                        data.IsReadOnly, data.IsTypeParameter, true));
                }
            }

            return rows;
        }

        private static Dictionary<string, string> ExtractParameters(Element el, HashSet<string> wanted)
        {
            Dictionary<string, string> pars = new();
            foreach (Parameter p in el.Parameters)
            {
                string name = p.Definition?.Name ?? string.Empty;
                if (name.Length == 0 || pars.ContainsKey(name) || !wanted.Contains(name)) continue;
                try { pars[name] = p.GetParameterValue() ?? string.Empty; }
                catch { pars[name] = string.Empty; }
            }
            return pars;
        }

        private static void AddParameterInfos(ParameterSet set, bool isType, Dictionary<string, CategoryParameterInfo> map)
        {
            foreach (Parameter p in set)
            {
                string name = p.Definition?.Name ?? string.Empty;
                if (name.Length == 0 || map.ContainsKey(name)) continue;
                map[name] = new CategoryParameterInfo
                {
                    Name = name,
                    StorageType = p.StorageType.ToString(),
                    IsReadOnly = p.IsReadOnly,
                    IsType = isType
                };
            }
        }

        private ElementFilter GetElementFilter(Document doc)
        {
            List<ElementFilter> filters = new List<ElementFilter>();

            foreach (BuiltInCategory item in GetBuildInModelCategories(doc))
            {
                filters.Add(new ElementCategoryFilter(item));
            }
            return new LogicalOrFilter(filters);
        }

        public List<BuiltInCategory> GetBuildInModelCategories(Document doc)
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
        public List<Category> GetModelCategories(Document doc)
        {
            Categories categories = doc.Settings.Categories;

            List<Category> result = categories.Cast<Category>()
                    .Where(cat => cat != null && cat.CategoryType == CategoryType.Model)
                    .ToList();

            return result;
        }
        public List<string> GetModelCategoriesNames(Document doc)
        {
            List<Category> categories = GetModelCategories(doc);
            List<string> result = new List<string>();

            foreach (Category cat in categories)
            {
                result.Add(cat.Name);
            }
            result.Sort();
            return result;
        }
    }
}
