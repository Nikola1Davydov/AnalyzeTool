using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.SharedParameters
{
    /// <summary>
    /// The numbers behind the A4 report: for each parameter, how many elements of its bound categories
    /// carry it, how many have a value — overall, per category, per level — and which values occur.
    /// Counted here rather than in the page: the page used to receive every parameter of every element
    /// and count in the browser, which is the whole model over the wire for a dozen numbers.
    /// </summary>
    internal sealed class ParameterReportService
    {
        private const string NoLevel = "(no level)";

        private static readonly BuiltInParameter[] LevelParameters =
        [
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.WALL_BASE_CONSTRAINT,
        ];

        public sealed record Request(Guid? Guid, string? Name);

        public ParameterReportResult Build(Document doc, IReadOnlyList<Request> requests, int topValues)
        {
            topValues = Math.Clamp(topValues, 1, 100);
            Dictionary<long, (string Name, double Elevation)> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .ToDictionary(l => l.Id.Value, l => (l.Name, l.Elevation));

            return new ParameterReportResult
            {
                DocumentTitle = doc.Title,
                Parameters = requests.Select(r => BuildOne(doc, r, topValues, levels)).ToList(),
            };
        }

        private static ParameterReportDto BuildOne(Document doc, Request request, int topValues,
            Dictionary<long, (string Name, double Elevation)> levels)
        {
            string label = request.Name ?? request.Guid?.ToString("D") ?? string.Empty;
            (InternalDefinition? def, ElementBinding? binding) = Resolve(doc, request);
            if (def is null)
                return new ParameterReportDto { Name = label, Guid = request.Guid?.ToString("D"), Error = "Not in this project." };
            if (binding is null || binding.Categories.IsEmpty)
                return new ParameterReportDto
                {
                    Name = def.Name, Guid = request.Guid?.ToString("D"),
                    Error = "Not bound to any category in this project (only loaded families carry it).",
                };

            List<ElementId> categoryIds = binding.Categories.Cast<Category>().Select(c => c.Id).ToList();
            IEnumerable<Element> elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(categoryIds));

            Dictionary<string, (int Total, int Filled)> byCategory = new();
            Dictionary<long, (int Total, int Filled)> byLevel = new();
            Dictionary<string, int> values = new();
            int total = 0, filled = 0;

            foreach (Element element in elements)
            {
                Parameter? parameter = element.get_Parameter(def);
                if (parameter is null && doc.GetElement(element.GetTypeId()) is ElementType type)
                    parameter = type.get_Parameter(def); // a type parameter: the instance shows its type's value
                if (parameter is null) continue;

                string value = ValueOf(parameter);
                bool hasValue = parameter.HasValue && !string.IsNullOrWhiteSpace(value);

                total++;
                if (hasValue)
                {
                    filled++;
                    values[value] = values.TryGetValue(value, out int n) ? n + 1 : 1;
                }

                string category = element.Category?.Name ?? string.Empty;
                (int t, int f) = byCategory.TryGetValue(category, out var c) ? c : (0, 0);
                byCategory[category] = (t + 1, f + (hasValue ? 1 : 0));

                long level = LevelOf(element).Value;
                (int lt, int lf) = byLevel.TryGetValue(level, out var l) ? l : (0, 0);
                byLevel[level] = (lt + 1, lf + (hasValue ? 1 : 0));
            }

            List<ReportValueDto> top = values
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase)
                .Take(topValues)
                .Select(kv => new ReportValueDto { Value = kv.Key, Count = kv.Value })
                .ToList();

            return new ParameterReportDto
            {
                Name = def.Name,
                Guid = (doc.GetElement(def.Id) as SharedParameterElement)?.GuidValue.ToString("D"),
                IsInstance = binding is InstanceBinding,
                Total = total,
                Filled = filled,
                Categories = byCategory
                    .OrderByDescending(kv => kv.Value.Total).ThenBy(kv => kv.Key)
                    .Select(kv => new ReportCountDto { Name = kv.Key, Total = kv.Value.Total, Filled = kv.Value.Filled })
                    .ToList(),
                Levels = byLevel
                    .OrderBy(kv => levels.TryGetValue(kv.Key, out var lv) ? lv.Elevation : double.MaxValue)
                    .Select(kv => new ReportCountDto
                    {
                        Name = levels.TryGetValue(kv.Key, out var lv) ? lv.Name : NoLevel,
                        Total = kv.Value.Total,
                        Filled = kv.Value.Filled,
                    })
                    .ToList(),
                Values = top,
                OtherValuesCount = filled - top.Sum(v => v.Count),
                DistinctValues = values.Count,
            };
        }

        /// <summary>The parameter's definition and binding: by GUID for a shared parameter, else by name
        /// among the bound ones.</summary>
        private static (InternalDefinition?, ElementBinding?) Resolve(Document doc, Request request)
        {
            if (request.Guid is Guid guid)
            {
                InternalDefinition? def = SharedParameterElement.Lookup(doc, guid)?.GetDefinition();
                return (def, def is null ? null : doc.ParameterBindings.get_Item(def) as ElementBinding);
            }

            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
                if (it.Key is InternalDefinition def && string.Equals(def.Name, request.Name, StringComparison.Ordinal))
                    return (def, it.Current as ElementBinding);
            return (null, null);
        }

        /// <summary>The value as Revit shows it: with units, Yes/No, a type's name for an id.</summary>
        private static string ValueOf(Parameter parameter)
        {
            if (parameter.StorageType == StorageType.String) return parameter.AsString() ?? string.Empty;
            if (parameter.StorageType == StorageType.ElementId && parameter.AsElementId() == ElementId.InvalidElementId)
                return string.Empty;
            return parameter.AsValueString() ?? parameter.GetParameterValue();
        }

        /// <summary>An element's level: its own LevelId, else the level parameter its kind uses (family
        /// instances, beams, walls…). InvalidElementId when it has none.</summary>
        private static ElementId LevelOf(Element element)
        {
            if (element.LevelId != ElementId.InvalidElementId) return element.LevelId;
            foreach (BuiltInParameter bip in LevelParameters)
            {
                Parameter? p = element.get_Parameter(bip);
                if (p?.StorageType == StorageType.ElementId && p.AsElementId() != ElementId.InvalidElementId)
                    return p.AsElementId();
            }
            return ElementId.InvalidElementId;
        }
    }
}
