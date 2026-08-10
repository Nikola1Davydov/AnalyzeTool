using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>
    /// The session's first call. An agent otherwise starts blind and spends three or four calls
    /// rediscovering the same facts — which language the category names are in, whether lengths are
    /// millimetres or feet, which levels exist, and where the model actually has geometry rather
    /// than just a type palette.
    ///
    /// Every field here answers a question that has been observed to go wrong when guessed:
    /// <list type="bullet">
    /// <item><c>language</c> — category names are LOCALISED. On a German install the category is
    ///       "Wände", and a filter for "Walls" silently returns nothing.</item>
    /// <item><c>displayUnits</c> — the millimetres-versus-internal-feet ambiguity, settled up front.
    ///       Every length in this answer is already expressed in these units.</item>
    /// <item><c>categoryCounts</c> — counts INSTANCES, so it shows where the model has geometry.
    ///       A category absent from this map has no instances, whatever types exist for it.</item>
    /// <item><c>activeView</c> / <c>levels</c> — what placement needs before it can propose a point.</item>
    /// </list>
    /// </summary>
    [RevitCommand(
        Description = "START HERE — the cheapest way to stop guessing. One call returns the document's " +
                      "title, Revit version, UI language, display units, workshared flag, active view, " +
                      "levels and a per-category INSTANCE count. Call it first in a session: category " +
                      "names are localised (on a German model 'Wände', not 'Walls') and lengths follow " +
                      "the model's display units, both of which every other command depends on. " +
                      "Read-only. Cost: scans the model-category instances once, so it is proportional " +
                      "to model size — call it once and keep the answer.",
        ReadOnly = true,
        OutputType = typeof(ModelOverview))]
    internal sealed class GetModelOverview : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;

                // Resolved first: every length below is reported in these units, so the caller never has
                // to know that Revit stores them as feet.
                ForgeTypeId lengthUnit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();

                DataElementsCollectorService collector = new DataElementsCollectorService();
                List<Category> modelCategories = collector.GetModelCategories(doc);

                Dictionary<string, int> categoryCounts = CountInstances(doc, collector, modelCategories);

                // Asked of the CATEGORY, not of the counted name: the name is localised, the built-in
                // category is not, and this flag decides whether RPC planting can be placed at all.
                bool hasTopography = modelCategories.Any(category =>
                    category.BuiltInCategory == BuiltInCategory.OST_Topography &&
                    categoryCounts.TryGetValue(category.Name, out int count) && count > 0);

                List<LevelInfo> levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(level => level.Elevation)
                    .Select(level => new LevelInfo(
                        level.Id.Value,
                        level.Name,
                        UnitUtils.ConvertFromInternalUnits(level.Elevation, lengthUnit)))
                    .ToList();

                View? activeView = doc.ActiveView;
                ActiveViewInfo? activeViewInfo = activeView is null
                    ? null
                    : new ActiveViewInfo(
                        activeView.Name,
                        activeView.ViewType.ToString(),
                        (activeView as ViewPlan)?.GenLevel?.Id.Value);

                return new ModelOverview(
                    doc.Title,
                    doc.Application.VersionNumber,
                    doc.Application.Language.ToString(),
                    ShortUnitName(lengthUnit),
                    doc.IsWorkshared,
                    activeViewInfo,
                    levels,
                    categoryCounts,
                    hasTopography);
            });

        /// <summary>Instances per model category, in ONE pass over the document. Types are excluded on
        /// purpose: a category with 40 wall types and no walls is an empty category to anyone asking
        /// where the building is.</summary>
        private static Dictionary<string, int> CountInstances(
            Document doc, DataElementsCollectorService collector, List<Category> modelCategories)
        {
            List<ElementFilter> filters = collector.GetBuildInModelCategories(doc)
                .Select(builtInCategory => (ElementFilter)new ElementCategoryFilter(builtInCategory))
                .ToList();
            // LogicalOrFilter rejects an empty list, and a document with no model categories is a
            // legitimate (if odd) answer rather than an error.
            if (filters.Count == 0) return new Dictionary<string, int>();

            Dictionary<long, string> namesById = new Dictionary<long, string>();
            foreach (Category category in modelCategories) namesById[category.Id.Value] = category.Name;

            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (Element element in new FilteredElementCollector(doc)
                         .WherePasses(new LogicalOrFilter(filters))
                         .WhereElementIsNotElementType())
            {
                Category? category = element.Category;
                if (category is null) continue;
                if (!namesById.TryGetValue(category.Id.Value, out string? name)) name = category.Name;

                counts.TryGetValue(name, out int current);
                counts[name] = current + 1;
            }
            return counts;
        }

        /// <summary>"autodesk.unit.unit:millimeters-1.0.1" → "millimeters". The full Forge id is precise
        /// but unreadable, and its localised label would reintroduce the language problem this command
        /// exists to remove.</summary>
        private static string ShortUnitName(ForgeTypeId? unit)
        {
            string id = unit?.TypeId ?? string.Empty;
            int colon = id.IndexOf(':');
            if (colon < 0) return id;

            string tail = id.Substring(colon + 1);
            int dash = tail.IndexOf('-');
            return dash < 0 ? tail : tail.Substring(0, dash);
        }
    }

    /// <summary>One level, with its elevation already in the model's display units.</summary>
    internal sealed record LevelInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("elevation")] double Elevation);

    /// <summary>The view a placement would land in. <see cref="LevelId"/> is null for views that have no
    /// generating level (3D, elevations, drafting) — which is itself the answer to "can I place here".</summary>
    internal sealed record ActiveViewInfo(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("type")] string Type,
        [property: JsonProperty("levelId")] long? LevelId);

    /// <summary>What a session needs to know before its first real question.</summary>
    internal sealed record ModelOverview(
        [property: JsonProperty("title")] string Title,
        [property: JsonProperty("revitVersion")] string RevitVersion,
        [property: JsonProperty("language")] string Language,
        [property: JsonProperty("displayUnits")] string DisplayUnits,
        [property: JsonProperty("isWorkshared")] bool IsWorkshared,
        [property: JsonProperty("activeView")] ActiveViewInfo? ActiveView,
        [property: JsonProperty("levels")] IReadOnlyList<LevelInfo> Levels,
        [property: JsonProperty("categoryCounts")] IReadOnlyDictionary<string, int> CategoryCounts,
        [property: JsonProperty("hasTopography")] bool HasTopography);
}
