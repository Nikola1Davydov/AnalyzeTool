using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Services.SharedParameters;

/// <summary>A parameter to evaluate: by GUID (shared) or by name (project parameter).</summary>
/// <param name="Guid">The GUID of a shared parameter.</param>
/// <param name="Name">The name of a project parameter.</param>
public sealed record ParameterReportRequest(Guid? Guid, string? Name);

/// <summary>
///     The numbers behind the report: for each parameter, how many elements of its bound categories carry it and how many
///     have a value — overall, per category, per level — and which values occur. Counted here rather than in the page, so
///     the model does not travel over the wire for a dozen numbers. Call it on the Revit thread.
/// </summary>
public sealed class ParameterReportService
{
    private const string NoLevel = "(no level)";

    private static readonly BuiltInParameter[] LevelParameters =
    [
        BuiltInParameter.FAMILY_LEVEL_PARAM,
        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
        BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
        BuiltInParameter.SCHEDULE_LEVEL_PARAM,
        BuiltInParameter.WALL_BASE_CONSTRAINT
    ];

    /// <summary>Evaluates every requested parameter, in request order.</summary>
    public ParameterReportResult Build(Document document, IReadOnlyList<ParameterReportRequest> requests, int topValues)
    {
        var levels = GetLevels(document);
        return new ParameterReportResult(document.Title, requests.Select(request => BuildOne(document, request, topValues, levels)).ToList());
    }

    /// <summary>Evaluates one parameter; <paramref name="topValues" /> is clamped to 1–100.</summary>
    public ParameterReportDto BuildOne(Document document, ParameterReportRequest request, int topValues)
    {
        return BuildOne(document, request, topValues, GetLevels(document));
    }

    private static Dictionary<long, (string Name, double Elevation)> GetLevels(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .ToDictionary(level => level.Id.Value, level => (level.Name, level.Elevation));
    }

    private static ParameterReportDto BuildOne(
        Document document,
        ParameterReportRequest request,
        int topValues,
        Dictionary<long, (string Name, double Elevation)> levels)
    {
        topValues = Math.Clamp(topValues, 1, 100);
        var label = request.Name ?? request.Guid?.ToString("D") ?? string.Empty;
        var (definition, binding) = Resolve(document, request);
        if (definition is null)
        {
            return new ParameterReportDto { Name = label, Guid = request.Guid?.ToString("D"), Error = "Not in this project." };
        }

        if (binding is null || binding.Categories.IsEmpty)
        {
            return new ParameterReportDto
            {
                Name = definition.Name,
                Guid = request.Guid?.ToString("D"),
                Error = "Not bound to any category in this project (only loaded families carry it)."
            };
        }

        var categoryIds = binding.Categories.Cast<Category>().Select(category => category.Id).ToList();
        var elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds));

        var byCategory = new Dictionary<string, (int Total, int Filled)>();
        var byLevel = new Dictionary<long, (int Total, int Filled)>();
        var values = new Dictionary<string, int>();
        var total = 0;
        var filled = 0;

        foreach (var element in elements)
        {
            var parameter = element.get_Parameter(definition);
            if (parameter is null && document.GetElement(element.GetTypeId()) is ElementType type)
            {
                parameter = type.get_Parameter(definition); // a type parameter: the instance shows its type's value
            }

            if (parameter is null)
            {
                continue;
            }

            var value = ValueOf(parameter);
            var hasValue = parameter.HasValue && !string.IsNullOrWhiteSpace(value);

            total++;
            if (hasValue)
            {
                filled++;
                values[value] = values.GetValueOrDefault(value) + 1;
            }

            var category = element.Category?.Name ?? string.Empty;
            var (categoryTotal, categoryFilled) = byCategory.GetValueOrDefault(category);
            byCategory[category] = (categoryTotal + 1, categoryFilled + (hasValue ? 1 : 0));

            var level = LevelOf(element).Value;
            var (levelTotal, levelFilled) = byLevel.GetValueOrDefault(level);
            byLevel[level] = (levelTotal + 1, levelFilled + (hasValue ? 1 : 0));
        }

        var top = values
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(topValues)
            .Select(pair => new ReportValueDto(pair.Key, pair.Value))
            .ToList();

        return new ParameterReportDto
        {
            Name = definition.Name,
            Guid = (document.GetElement(definition.Id) as SharedParameterElement)?.GuidValue.ToString("D"),
            IsInstance = binding is InstanceBinding,
            Total = total,
            Filled = filled,
            Categories = byCategory
                .OrderByDescending(pair => pair.Value.Total)
                .ThenBy(pair => pair.Key)
                .Select(pair => new ReportCountDto(pair.Key, pair.Value.Total, pair.Value.Filled))
                .ToList(),
            Levels = byLevel
                .OrderBy(pair => levels.TryGetValue(pair.Key, out var level) ? level.Elevation : double.MaxValue)
                .Select(pair => new ReportCountDto(
                    levels.TryGetValue(pair.Key, out var level) ? level.Name : NoLevel,
                    pair.Value.Total,
                    pair.Value.Filled))
                .ToList(),
            Values = top,
            OtherValuesCount = filled - top.Sum(value => value.Count),
            DistinctValues = values.Count
        };
    }

    /// <summary>The definition and binding: by GUID for a shared parameter, else by name among the bound ones.</summary>
    private static (InternalDefinition? Definition, ElementBinding? Binding) Resolve(Document document, ParameterReportRequest request)
    {
        if (request.Guid is { } guid)
        {
            var definition = SharedParameterElement.Lookup(document, guid)?.GetDefinition();
            return (definition, definition is null ? null : document.ParameterBindings.get_Item(definition) as ElementBinding);
        }

        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is InternalDefinition definition && string.Equals(definition.Name, request.Name, StringComparison.Ordinal))
            {
                return (definition, iterator.Current as ElementBinding);
            }
        }

        return (null, null);
    }

    /// <summary>The value as Revit shows it: with units, Yes/No, a type's name for an id.</summary>
    private static string ValueOf(Parameter parameter)
    {
        if (parameter.StorageType == StorageType.String)
        {
            return parameter.AsString() ?? string.Empty;
        }

        if (parameter.StorageType == StorageType.ElementId && parameter.AsElementId() == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        return parameter.AsValueString() ?? parameter.GetParameterValue();
    }

    /// <summary>The element's own level, else the level parameter its kind uses; invalid when it has none.</summary>
    private static ElementId LevelOf(Element element)
    {
        if (element.LevelId != ElementId.InvalidElementId)
        {
            return element.LevelId;
        }

        foreach (var builtInParameter in LevelParameters)
        {
            var parameter = element.get_Parameter(builtInParameter);
            if (parameter?.StorageType == StorageType.ElementId && parameter.AsElementId() != ElementId.InvalidElementId)
            {
                return parameter.AsElementId();
            }
        }

        return ElementId.InvalidElementId;
    }
}
