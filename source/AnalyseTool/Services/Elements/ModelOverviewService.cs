namespace AnalyseTool.Services.Elements;

/// <summary>One level, with its elevation in the model's display units.</summary>
/// <param name="Id">The level id.</param>
/// <param name="Name">The level name.</param>
/// <param name="Elevation">The elevation in <see cref="ModelOverview.DisplayUnits" />.</param>
public sealed record LevelInfo(long Id, string Name, double Elevation);

/// <summary>The active view; <see cref="LevelId" /> is null for views without a generating level.</summary>
/// <param name="Name">The view name.</param>
/// <param name="Type">The <c>ViewType</c> name.</param>
/// <param name="LevelId">The generating level of a plan; null otherwise.</param>
public sealed record ActiveViewInfo(string Name, string Type, long? LevelId);

/// <summary>A model category that has instances.</summary>
/// <param name="Name">The localised name a person sees.</param>
/// <param name="BuiltInCategory">The <c>BuiltInCategory</c> a command should be given.</param>
/// <param name="Count">How many instances.</param>
public sealed record CategoryInstanceCount(string Name, string BuiltInCategory, int Count);

/// <summary>What a session needs to know before its first real question.</summary>
/// <param name="Title">The document title.</param>
/// <param name="RevitVersion">The Revit version number.</param>
/// <param name="Language">The UI language of Revit.</param>
/// <param name="DisplayUnits">The display unit of lengths (<c>millimeters</c>…); every length here is in it.</param>
/// <param name="IsWorkshared">Whether the document is workshared.</param>
/// <param name="ActiveView">The active view; null when there is none.</param>
/// <param name="Levels">The levels, bottom to top.</param>
/// <param name="CategoryCounts">Localised category name → instance count; a category without instances is absent.</param>
/// <param name="Categories">The same counts with the <c>BuiltInCategory</c>, most first.</param>
/// <param name="HasTopography">Whether the model has topography instances.</param>
public sealed record ModelOverview(
    string Title,
    string RevitVersion,
    string Language,
    string DisplayUnits,
    bool IsWorkshared,
    ActiveViewInfo? ActiveView,
    IReadOnlyList<LevelInfo> Levels,
    IReadOnlyDictionary<string, int> CategoryCounts,
    IReadOnlyList<CategoryInstanceCount> Categories,
    bool HasTopography);

/// <summary>
///     The first call of a session: language, units, levels and where the model has instances. Call it on the Revit thread.
/// </summary>
public sealed class ModelOverviewService(DataElementsCollectorService collector)
{
    /// <summary>The overview of <paramref name="document" />.</summary>
    public ModelOverview GetOverview(Document document)
    {
        // Every length below is reported in these units
        var lengthUnit = document.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();

        var modelCategories = collector.GetModelCategories(document);
        var categoryCounts = CountInstances(document, modelCategories);

        // Asked of the category, not of the localised name
        var hasTopography = modelCategories.Any(category =>
            category.BuiltInCategory == BuiltInCategory.OST_Topography &&
            categoryCounts.TryGetValue(category.Name, out var count) && count > 0);

        var levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .Select(level => new LevelInfo(level.Id.Value, level.Name, UnitUtils.ConvertFromInternalUnits(level.Elevation, lengthUnit)))
            .ToList();

        var activeView = document.ActiveView;
        var activeViewInfo = activeView is null
            ? null
            : new ActiveViewInfo(activeView.Name, activeView.ViewType.ToString(), (activeView as ViewPlan)?.GenLevel?.Id.Value);

        var categories = modelCategories
            .Where(category => categoryCounts.TryGetValue(category.Name, out var count) && count > 0)
            .Select(category => new CategoryInstanceCount(category.Name, category.BuiltInCategory.ToString(), categoryCounts[category.Name]))
            .OrderByDescending(category => category.Count)
            .ThenBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ModelOverview(
            document.Title,
            document.Application.VersionNumber,
            document.Application.Language.ToString(),
            ShortUnitName(lengthUnit),
            document.IsWorkshared,
            activeViewInfo,
            levels,
            categoryCounts,
            categories,
            hasTopography);
    }

    /// <summary>Instances per model category in one pass; types are excluded — 40 wall types and no walls is an empty category.</summary>
    private Dictionary<string, int> CountInstances(Document document, List<Category> modelCategories)
    {
        var filters = collector.GetBuiltInModelCategories(document)
            .Select(ElementFilter (builtInCategory) => new ElementCategoryFilter(builtInCategory))
            .ToList();
        // LogicalOrFilter rejects an empty list
        if (filters.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var namesById = new Dictionary<long, string>();
        foreach (var category in modelCategories)
        {
            namesById[category.Id.Value] = category.Name;
        }

        var counts = new Dictionary<string, int>();
        foreach (var element in new FilteredElementCollector(document).WherePasses(new LogicalOrFilter(filters)).WhereElementIsNotElementType())
        {
            if (element.Category is not { } category)
            {
                continue;
            }

            var name = namesById.GetValueOrDefault(category.Id.Value) ?? category.Name;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return counts;
    }

    /// <summary><c>autodesk.unit.unit:millimeters-1.0.1</c> → <c>millimeters</c>.</summary>
    internal static string ShortUnitName(ForgeTypeId? unit)
    {
        var id = unit?.TypeId ?? string.Empty;
        var colon = id.IndexOf(':');
        if (colon < 0)
        {
            return id;
        }

        var tail = id[(colon + 1)..];
        var dash = tail.IndexOf('-');
        return dash < 0 ? tail : tail[..dash];
    }
}
