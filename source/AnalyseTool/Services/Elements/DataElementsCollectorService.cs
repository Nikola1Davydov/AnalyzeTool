using System.Globalization;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Services.Elements;

/// <summary>
///     Element and category reads of the Elements slice, as functions of a <see cref="Document" />. Call them on the Revit
///     thread.
/// </summary>
public sealed class DataElementsCollectorService
{
    /// <summary>Placed elements.</summary>
    public const string KindInstances = "instances";

    /// <summary>The type palette.</summary>
    public const string KindTypes = "types";

    /// <summary>Both.</summary>
    public const string KindAll = "all";

    /// <summary>Every instance and type of the model category with this localised name, with all parameters.</summary>
    public IReadOnlyList<DataElement> GetAllElementsByCategory(Document document, string category)
    {
        var match = GetModelCategories(document).FirstOrDefault(item => item.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return [];
        }

        var builtInCategory = match.BuiltInCategory;
        var instances = new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsNotElementType();
        var types = new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsElementType();

        return instances.UnionWith(types).ToElements().Select(DataElement.Of).ToList();
    }

    /// <summary>
    ///     A lean, filterable listing of a category. The kind defaults to instances: system types sort first by id and would
    ///     otherwise fill a limited answer before a single placed element appeared.
    /// </summary>
    public ElementsResult GetElementSummaries(Document document, ElementQuery query)
    {
        var kind = (query.ElementKind ?? KindInstances).Trim().ToLowerInvariant();
        if (kind.Length == 0)
        {
            kind = KindInstances;
        }

        if (kind is not (KindInstances or KindTypes or KindAll))
        {
            return Empty(null, KindInstances, $"Unknown elementKind '{query.ElementKind}'. Use \"{KindInstances}\", \"{KindTypes}\" or \"{KindAll}\".");
        }

        var match = ResolveRequestedCategory(document, query, out var categoryError, out var didYouMean);
        if (match is null)
        {
            return Empty(null, kind, categoryError, didYouMean);
        }

        var builtInCategory = match.BuiltInCategory;
        var elements = new List<Element>();
        if (kind is KindInstances or KindAll)
        {
            elements.AddRange(new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsNotElementType().ToElements());
        }

        if (kind is KindTypes or KindAll)
        {
            elements.AddRange(new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsElementType().ToElements());
        }

        // Cached by type id: thousands of doors share a handful of types
        var typeCache = new Dictionary<long, ElementType?>();

        // Lazy: unfiltered, only the page is described, so the limit bounds the work and not only the answer
        var matching = elements.Select(element => Describe(document, element, typeCache));

        // nameContains matches the element's own name, its family name or its type name
        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            matching = matching.Where(item =>
                Contains(item.Element.Name, query.NameContains) ||
                Contains(item.FamilyName, query.NameContains) ||
                Contains(item.TypeName, query.NameContains));
        }

        if (!string.IsNullOrWhiteSpace(query.FamilyNameContains))
        {
            matching = matching.Where(item => Contains(item.FamilyName, query.FamilyNameContains));
        }

        if (!string.IsNullOrWhiteSpace(query.TypeNameContains))
        {
            matching = matching.Where(item => Contains(item.TypeName, query.TypeNameContains));
        }

        var filtered = !string.IsNullOrWhiteSpace(query.NameContains)
                       || !string.IsNullOrWhiteSpace(query.FamilyNameContains)
                       || !string.IsNullOrWhiteSpace(query.TypeNameContains);

        var total = elements.Count;
        if (filtered)
        {
            var hits = matching.ToList();
            total = hits.Count;
            matching = hits;
        }

        var page = query.Limit is > 0 ? matching.Take(query.Limit.Value) : matching;

        var wanted = query.ParameterNames is { Count: > 0 }
            ? new HashSet<string>(query.ParameterNames, StringComparer.OrdinalIgnoreCase)
            : null;

        var items = page
            .Select(item => new ElementItem(
                item.Element.Id.Value,
                item.Element.Name,
                item.Element.Category?.Name ?? string.Empty,
                BuiltInName(item.Element.Category),
                document.GetElement(item.Element.LevelId)?.Name ?? string.Empty,
                item.Element.LevelId == ElementId.InvalidElementId ? null : item.Element.LevelId.Value,
                item.Element is ElementType,
                item.FamilyId,
                item.FamilyName,
                item.TypeName,
                wanted is null ? null : ExtractParameters(item.Element, wanted)))
            .ToList();

        return new ElementsResult(match.Name, kind, total, items.Count, items, null, null);
    }

    /// <summary>
    ///     The parameters of a category, sampled from one element and its type; the category resolved as
    ///     <see cref="GetElementSummaries" /> does.
    /// </summary>
    public CategoryParametersResult GetCategoryParameters(Document document, ElementQuery query)
    {
        var match = ResolveRequestedCategory(document, query, out var error, out var didYouMean);
        if (match is null)
        {
            return new CategoryParametersResult(query.Category, query.BuiltInCategory, 0, [], error, didYouMean);
        }

        var parameters = GetCategoryParameterInfos(document, match);
        return new CategoryParametersResult(match.Name, match.BuiltInCategory.ToString(), parameters.Count, parameters, null, null);
    }

    /// <summary>The built-in categories of the model categories.</summary>
    public List<BuiltInCategory> GetBuiltInModelCategories(Document document)
    {
        return GetModelCategories(document).Select(category => category.BuiltInCategory).ToList();
    }

    /// <summary>The model categories of the document.</summary>
    public List<Category> GetModelCategories(Document document)
    {
        return document.Settings.Categories
            .Cast<Category>()
            .Where(category => category is not null && category.CategoryType == CategoryType.Model)
            .ToList();
    }

    /// <summary>The localised names of the model categories, sorted.</summary>
    public List<string> GetModelCategoriesNames(Document document)
    {
        var names = GetModelCategories(document).Select(category => category.Name).ToList();
        names.Sort();
        return names;
    }

    private static ElementsResult Empty(string? category, string kind, string? error, IReadOnlyList<string>? didYouMean = null)
    {
        return new ElementsResult(category, kind, 0, 0, [], error, didYouMean);
    }

    /// <summary>Ordinal for filtering: a filter has to be predictable.</summary>
    private static bool Contains(string? haystack, string? needle)
    {
        return haystack is not null && needle is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Diacritic-insensitive, for suggesting only: «Wand» has to find «Wände».</summary>
    private static bool ContainsLoose(string? haystack, string? needle)
    {
        return haystack is not null && needle is { Length: > 0 } &&
               CultureInfo.CurrentCulture.CompareInfo.IndexOf(haystack, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    /// <summary>The family and type of an element; a system family has a name and no family id.</summary>
    private static (Element Element, long? FamilyId, string? FamilyName, string? TypeName) Describe(
        Document document, Element element, Dictionary<long, ElementType?> typeCache)
    {
        var type = element as ElementType;
        if (type is null)
        {
            var typeId = element.GetTypeId();
            if (!typeCache.TryGetValue(typeId.Value, out type))
            {
                type = document.GetElement(typeId) as ElementType;
                typeCache[typeId.Value] = type;
            }
        }

        var family = (element as FamilyInstance)?.Symbol?.Family ?? (type as FamilySymbol)?.Family;
        var familyName = type is not null && !string.IsNullOrWhiteSpace(type.FamilyName) ? type.FamilyName : null;

        return (element, family?.Id.Value, familyName, type?.Name);
    }

    /// <summary>
    ///     The category from the <c>BuiltInCategory</c> name or the localised one. An unknown one is reported, with near
    ///     names: an empty list reads exactly like an empty category.
    /// </summary>
    private Category? ResolveRequestedCategory(Document document, ElementQuery query, out string? error, out List<string>? didYouMean)
    {
        error = null;
        didYouMean = null;
        var categories = GetModelCategories(document);

        if (!string.IsNullOrWhiteSpace(query.BuiltInCategory))
        {
            if (!Enum.TryParse(query.BuiltInCategory, ignoreCase: true, out BuiltInCategory parsed))
            {
                error = $"'{query.BuiltInCategory}' is not a BuiltInCategory name. They look like \"OST_Walls\".";
                return null;
            }

            var byBuiltIn = categories.FirstOrDefault(category => category.BuiltInCategory == parsed);
            if (byBuiltIn is null)
            {
                error = $"The model has no category {query.BuiltInCategory}.";
            }

            return byBuiltIn;
        }

        if (string.IsNullOrWhiteSpace(query.Category))
        {
            error = "Pass either category (the localised name) or builtInCategory (e.g. \"OST_Walls\").";
            return null;
        }

        var byName = categories.FirstOrDefault(category => category.Name.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        // Substring both ways: localised names are often compound, «Wand» → «Wände», «Tür» → «Türen»
        didYouMean = categories
            .Select(category => category.Name)
            .Where(name => ContainsLoose(name, query.Category) || ContainsLoose(query.Category, name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Take(5)
            .ToList();
        if (didYouMean.Count == 0)
        {
            didYouMean = null;
        }

        error = $"No category named '{query.Category}' in this model. Category names are LOCALISED — " +
                "GetModelOverview lists the ones that have elements, GetCategoriesInRevit lists them all, " +
                "and builtInCategory (e.g. \"OST_Walls\") avoids the question entirely.";
        return null;
    }

    /// <summary>The parameters of a sample element and, for an instance, of its type.</summary>
    private static List<CategoryParameterInfo> GetCategoryParameterInfos(Document document, Category match)
    {
        var builtInCategory = match.BuiltInCategory;
        var sample = new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsNotElementType().FirstElement()
                     ?? new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsElementType().FirstElement();
        if (sample is null)
        {
            return [];
        }

        var map = new Dictionary<string, CategoryParameterInfo>(StringComparer.OrdinalIgnoreCase);
        // By what the sample is: a level, a grid or a room has no type either, and is not one
        var sampleIsType = sample is ElementType;
        AddParameterInfos(sample.Parameters, sampleIsType, map);

        if (!sampleIsType && document.GetElement(sample.GetTypeId()) is { } type)
        {
            AddParameterInfos(type.Parameters, true, map);
        }

        return map.Values.OrderBy(parameter => parameter.Name).ToList();
    }

    /// <summary><c>OST_Walls</c> for a built-in category, null for anything else.</summary>
    private static string? BuiltInName(Category? category)
    {
        if (category is null)
        {
            return null;
        }

        var builtInCategory = category.BuiltInCategory;
        return builtInCategory == BuiltInCategory.INVALID || !Enum.IsDefined(builtInCategory) ? null : builtInCategory.ToString();
    }

    private static Dictionary<string, string> ExtractParameters(Element element, HashSet<string> wanted)
    {
        var values = new Dictionary<string, string>();
        foreach (Parameter parameter in element.Parameters)
        {
            var name = parameter.Definition?.Name ?? string.Empty;
            if (name.Length == 0 || values.ContainsKey(name) || !wanted.Contains(name))
            {
                continue;
            }

            try
            {
                values[name] = parameter.GetParameterValue();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                values[name] = string.Empty;
            }
        }

        return values;
    }

    private static void AddParameterInfos(ParameterSet parameters, bool isType, Dictionary<string, CategoryParameterInfo> map)
    {
        foreach (Parameter parameter in parameters)
        {
            var name = parameter.Definition?.Name ?? string.Empty;
            if (name.Length == 0 || map.ContainsKey(name))
            {
                continue;
            }

            var builtIn = ParameterUtils.IsBuiltInParameter(parameter.Id);
            var (spec, unit) = parameter.DescribeUnits();
            map[name] = new CategoryParameterInfo(
                parameter.Id.Value,
                builtIn ? ((BuiltInParameter)parameter.Id.Value).ToString() : null,
                name,
                parameter.StorageType.ToString(),
                spec,
                unit,
                parameter.IsReadOnly,
                isType);
        }
    }
}
