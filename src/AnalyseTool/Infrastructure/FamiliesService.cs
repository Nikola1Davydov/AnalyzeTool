using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Infrastructure
{
    /// <summary>Collects family-related information from a document.</summary>
    public sealed class FamiliesService
    {
        /// <summary>In-place family instances (modelled in the project, not loaded from a .rfa).</summary>
        public List<FamilyInstanceInfo> GetInPlaceFamilies(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol.Family.IsInPlace)
                .Select(fi => new FamilyInstanceInfo(fi.Id.Value, fi.Name, fi.Category?.Name ?? string.Empty))
                .ToList();

        /// <summary>
        /// Inventory of the loadable/in-place families in the document: category, type count and the
        /// number of placed instances, plus an in-place flag. Backs the Family Control table. Optional
        /// case-insensitive filters on category/name; <paramref name="limit"/> caps the rows returned
        /// (the unfiltered <see cref="FamilyInventory.Count"/> is still reported).
        /// </summary>
        public FamilyInventory GetFamilies(Document doc, string? categoryContains, string? nameContains, int? limit)
        {
            Dictionary<long, int> instanceCounts = CountInstancesPerFamily(doc);

            List<FamilyInfo> all = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Select(family => new FamilyInfo(
                    family.Id.Value,
                    family.UniqueId,
                    family.VersionGuid.ToString(),
                    family.Name,
                    family.FamilyCategory?.Name ?? string.Empty,
                    family.GetFamilySymbolIds().Count,
                    instanceCounts.TryGetValue(family.Id.Value, out int count) ? count : 0,
                    family.IsInPlace))
                .Where(r => Matches(r.Category, categoryContains) && Matches(r.Name, nameContains))
                .OrderBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = all.Count;
            List<FamilyInfo> rows = limit is int lim and > 0 && all.Count > lim ? all.Take(lim).ToList() : all;
            return new FamilyInventory(total, rows.Count, rows);
        }

        /// <summary>One pass over all placed instances → instance count keyed by owning family id.</summary>
        private static Dictionary<long, int> CountInstancesPerFamily(Document doc)
        {
            Dictionary<long, int> counts = new();
            foreach (FamilyInstance instance in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                ElementId? familyId = instance.Symbol?.Family?.Id;
                if (familyId is null) continue;
                counts.TryGetValue(familyId.Value, out int current);
                counts[familyId.Value] = current + 1;
            }
            return counts;
        }

        private static bool Matches(string value, string? contains) =>
            string.IsNullOrWhiteSpace(contains) ||
            value.Contains(contains, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The types (FamilySymbols) of one family, each with its placed-instance count and its
        /// non-empty type parameters. Backs the Family Control detail panel.
        /// </summary>
        public FamilyTypesResult GetFamilyTypes(Document doc, long familyId)
        {
            if (doc.GetElement(new ElementId(familyId)) is not Family family)
                return new FamilyTypesResult(familyId, string.Empty, string.Empty, new List<FamilyTypeInfo>());

            Dictionary<long, int> counts = new();
            foreach (FamilyInstance fi in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                ElementId? symbolId = fi.Symbol?.Id;
                if (symbolId is null) continue;
                counts.TryGetValue(symbolId.Value, out int c);
                counts[symbolId.Value] = c + 1;
            }

            List<FamilyTypeInfo> types = family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(symbol => symbol is not null)
                .Select(symbol => new FamilyTypeInfo(
                    symbol!.Id.Value,
                    symbol.Name,
                    counts.TryGetValue(symbol.Id.Value, out int n) ? n : 0,
                    ReadTypeParameters(symbol)))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new FamilyTypesResult(
                familyId, family.Name, family.FamilyCategory?.Name ?? string.Empty, types);
        }

        /// <summary>Non-empty parameters of a family type, as plain name/value pairs for the UI.</summary>
        private static List<FamilyParameterInfo> ReadTypeParameters(Element type)
        {
            List<FamilyParameterInfo> list = new();
            foreach (Parameter p in type.Parameters)
            {
                if (p?.Definition is null) continue;
                string value = p.AsValueString() ?? p.AsString() ?? string.Empty;
                if (string.IsNullOrEmpty(value)) continue;
                list.Add(new FamilyParameterInfo(p.Definition.Name, value));
            }
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public sealed record FamilyInstanceInfo(long Id, string Name, string Category);

    public sealed record FamilyInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("uniqueId")] string UniqueId,
        [property: JsonProperty("versionGuid")] string VersionGuid,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("typeCount")] int TypeCount,
        [property: JsonProperty("instanceCount")] int InstanceCount,
        [property: JsonProperty("isInPlace")] bool IsInPlace);

    public sealed record FamilyInventory(
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("returned")] int Returned,
        [property: JsonProperty("families")] IReadOnlyList<FamilyInfo> Families);

    public sealed record FamilyParameterInfo(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("value")] string Value);

    public sealed record FamilyTypeInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("instanceCount")] int InstanceCount,
        [property: JsonProperty("parameters")] IReadOnlyList<FamilyParameterInfo> Parameters);

    public sealed record FamilyTypesResult(
        [property: JsonProperty("familyId")] long FamilyId,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("types")] IReadOnlyList<FamilyTypeInfo> Types);
}
