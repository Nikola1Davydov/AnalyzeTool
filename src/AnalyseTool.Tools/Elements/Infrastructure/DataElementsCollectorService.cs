using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using System.Globalization;

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
        public const string KindInstances = "instances";
        public const string KindTypes = "types";
        public const string KindAll = "all";

        /// <summary>
        /// Lean, filterable element listing for AI/MCP callers.
        /// </summary>
        /// <remarks>
        /// This used to union instances and types and then Take(limit) off the front. Element ids run
        /// roughly in creation order and system TYPES are created with the document, so they sort first:
        /// asking for 10 walls returned 10 WallTypes and not one wall, and the caller could not tell,
        /// because "isType" was the only clue and nothing said how many had been left behind. Kind is now
        /// chosen explicitly and defaults to instances — the thing a question about a model is about.
        ///
        /// isType comes from WHICH collector produced the element, not from GetTypeId() == invalid. That
        /// old test also called every type-less instance a type: levels and grids have no type, so they
        /// were all reported as types.
        /// </remarks>
        public ElementsResult GetElementSummaries(Document doc, ElementQuery query)
        {
            string kind = (query.ElementKind ?? KindInstances).Trim().ToLowerInvariant();
            if (kind.Length == 0) kind = KindInstances;
            if (kind != KindInstances && kind != KindTypes && kind != KindAll)
                return Empty(null, KindInstances,
                    $"Unknown elementKind '{query.ElementKind}'. Use \"{KindInstances}\", \"{KindTypes}\" or \"{KindAll}\".");

            Category? match = ResolveRequestedCategory(doc, query, out string? categoryError, out List<string>? didYouMean);
            if (match == null) return Empty(null, kind, categoryError, didYouMean);

            BuiltInCategory bic = match.BuiltInCategory;
            List<Element> elements = new();
            if (kind is KindInstances or KindAll)
                elements.AddRange(new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().ToElements());
            if (kind is KindTypes or KindAll)
                elements.AddRange(new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsElementType().ToElements());

            // Cached by type id: a category of 5000 doors has a handful of types between them, and each
            // Describe is a document lookup (the element's type, and for a family instance its family).
            Dictionary<long, ElementType?> typeCache = new();

            // LAZY on purpose. The filters below ask about family and type names, so a filtered query
            // has to describe every element to know what matches. An UNfiltered one does not: nothing
            // is pulled through this Select until the page is taken, so Describe runs `limit` times
            // rather than once per element in the category — which is what makes limit bound the WORK
            // and not merely the answer, as the command's own description promises.
            IEnumerable<(Element Element, long? FamilyId, string? FamilyName, string? TypeName)> matching =
                elements.Select(el => Describe(doc, el, typeCache));

            // nameContains matches the element's own name, its FAMILY name or its TYPE name. Narrower was
            // the bug: "Laub" over Bepflanzung found nothing, because the family is "Baum RPC - Laubbaum"
            // while its types are named "Japanischer Ahorn - 3,0 Meter", and only type names were searched.
            if (!string.IsNullOrWhiteSpace(query.NameContains))
                matching = matching.Where(d =>
                    Contains(d.Element.Name, query.NameContains) ||
                    Contains(d.FamilyName, query.NameContains) ||
                    Contains(d.TypeName, query.NameContains));

            if (!string.IsNullOrWhiteSpace(query.FamilyNameContains))
                matching = matching.Where(d => Contains(d.FamilyName, query.FamilyNameContains));

            if (!string.IsNullOrWhiteSpace(query.TypeNameContains))
                matching = matching.Where(d => Contains(d.TypeName, query.TypeNameContains));

            // The reported total needs every element only when a filter decides membership. With none it
            // is the count already in hand, and materialising the list to learn a number we know would
            // describe the whole category for nothing.
            bool filtered = !string.IsNullOrWhiteSpace(query.NameContains)
                            || !string.IsNullOrWhiteSpace(query.FamilyNameContains)
                            || !string.IsNullOrWhiteSpace(query.TypeNameContains);

            int total = elements.Count;
            if (filtered)
            {
                List<(Element Element, long? FamilyId, string? FamilyName, string? TypeName)> hits = matching.ToList();
                total = hits.Count;
                matching = hits;
            }

            IEnumerable<(Element Element, long? FamilyId, string? FamilyName, string? TypeName)> page = matching;
            if (query.Limit is > 0) page = page.Take(query.Limit.Value);

            HashSet<string>? wanted = (query.ParameterNames != null && query.ParameterNames.Count > 0)
                ? new HashSet<string>(query.ParameterNames, StringComparer.OrdinalIgnoreCase)
                : null;

            List<ElementSummary> summaries = new();
            foreach ((Element el, long? familyId, string? familyName, string? typeName) in page)
            {
                summaries.Add(new ElementSummary
                {
                    Id = el.Id.Value,
                    Name = el.Name,
                    Category = el.Category?.Name ?? string.Empty,
                    Level = doc.GetElement(el.LevelId)?.Name ?? string.Empty,
                    IsType = el is ElementType,
                    FamilyId = familyId,
                    FamilyName = familyName,
                    TypeName = typeName,
                    Parameters = wanted == null ? null : ExtractParameters(el, wanted)
                });
            }

            return new ElementsResult(match.Name, kind, total, summaries.Count, summaries, null, null);
        }

        private static ElementsResult Empty(
            string? category, string kind, string? error, IReadOnlyList<string>? didYouMean = null) =>
            new(category, kind, 0, 0, new List<ElementSummary>(), error, didYouMean);

        /// <summary>Ordinal for FILTERING: a filter has to be predictable, and a caller that asked for
        /// "Wand" should not silently also get "Wände".</summary>
        private static bool Contains(string? haystack, string? needle) =>
            haystack != null && needle != null &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Diacritic-insensitive, for SUGGESTING only — the opposite trade-off. "Wand" has to
        /// find "Wände" and "Tur" has to find "Türen": those are precisely the misses a non-German
        /// speaker makes against a German model, and a suggestion that cannot bridge an umlaut is no
        /// help at all. Never used to decide what a query RETURNS.</summary>
        private static bool ContainsLoose(string? haystack, string? needle) =>
            haystack != null && needle != null && needle.Length > 0 &&
            CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                haystack, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        /// <summary>Family and type of one element. A FamilyInstance knows its family directly; everything
        /// else goes through its ElementType, whose FamilyName covers SYSTEM families too (there is no
        /// Family element behind those, so the id stays null while the name is still useful).</summary>
        private static (Element Element, long? FamilyId, string? FamilyName, string? TypeName) Describe(
            Document doc, Element el, Dictionary<long, ElementType?> typeCache)
        {
            ElementType? type = el as ElementType;
            if (type == null)
            {
                long typeId = el.GetTypeId().Value;
                if (!typeCache.TryGetValue(typeId, out type))
                {
                    type = doc.GetElement(el.GetTypeId()) as ElementType;
                    typeCache[typeId] = type;
                }
            }

            Family? family = (el as FamilyInstance)?.Symbol?.Family ?? (type as FamilySymbol)?.Family;
            long? familyId = family?.Id.Value;

            string? familyName = type != null && !string.IsNullOrWhiteSpace(type.FamilyName) ? type.FamilyName : null;

            return (el, familyId, familyName, type?.Name);
        }

        /// <summary>
        /// Resolves the category from either the localised name or the language-independent
        /// BuiltInCategory. An unknown one is REPORTED, with near names where there are any: an empty list
        /// is indistinguishable from a category that happens to be empty, and that ambiguity is what makes
        /// a wrong category name expensive — the caller believes the answer.
        /// </summary>
        private Category? ResolveRequestedCategory(
            Document doc, ElementQuery query, out string? error, out List<string>? didYouMean)
        {
            error = null;
            didYouMean = null;
            List<Category> categories = GetModelCategories(doc);

            if (!string.IsNullOrWhiteSpace(query.BuiltInCategory))
            {
                if (!Enum.TryParse(query.BuiltInCategory, ignoreCase: true, out BuiltInCategory parsed))
                {
                    error = $"'{query.BuiltInCategory}' is not a BuiltInCategory name. They look like \"OST_Walls\".";
                    return null;
                }

                Category? byBuiltIn = categories.FirstOrDefault(c => c.BuiltInCategory == parsed);
                if (byBuiltIn == null)
                    error = $"The model has no category {query.BuiltInCategory}.";
                return byBuiltIn;
            }

            if (string.IsNullOrWhiteSpace(query.Category))
            {
                error = "Pass either category (the localised name) or builtInCategory (e.g. \"OST_Walls\").";
                return null;
            }

            Category? byName = categories.FirstOrDefault(c => c.Name.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;

            // Substring both ways rather than edit distance: category names are localised and often
            // compound, so "Wand" -> "Wände" and "Tür" -> "Türen" are the misses that actually happen.
            // Diacritic-insensitive, or the umlaut in that very example defeats the suggestion.
            didYouMean = categories
                .Select(c => c.Name)
                .Where(name => ContainsLoose(name, query.Category) || ContainsLoose(query.Category, name))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Take(5)
                .ToList();
            if (didYouMean.Count == 0) didYouMean = null;

            error = $"No category named '{query.Category}' in this model. Category names are LOCALISED — " +
                    "GetModelOverview lists the ones that have elements, GetCategoriesInRevit lists them all, " +
                    "and builtInCategory (e.g. \"OST_Walls\") avoids the question entirely.";
            return null;
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
