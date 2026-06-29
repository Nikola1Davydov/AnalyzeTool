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

        /// <summary>
        /// User worksets of the document (empty + <see cref="WorksetsResult.IsWorkshared"/>=false for a
        /// non-workshared project). Backs the Worksets view and the "edit workset" target picker.
        /// </summary>
        public WorksetsResult GetWorksets(Document doc)
        {
            if (!doc.IsWorkshared)
                return new WorksetsResult(false, new List<WorksetInfo>());

            List<WorksetInfo> worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .Select(ws => new WorksetInfo(
                    ws.Id.IntegerValue, ws.Name, ws.IsOpen, ws.IsEditable, ws.Owner ?? string.Empty))
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new WorksetsResult(true, worksets);
        }

        /// <summary>
        /// Placed instances filtered by owning family ids and/or type (FamilySymbol) ids, each with its
        /// type name, category, level and workset. Backs Select/Isolate wiring and workset reassignment.
        /// Returns nothing when both id sets are empty. <paramref name="limit"/> caps the rows.
        /// </summary>
        public FamilyInstancesResult GetFamilyInstances(
            Document doc, IEnumerable<long> familyIds, IEnumerable<long> typeIds, int? limit)
        {
            HashSet<long> fids = new(familyIds);
            HashSet<long> tids = new(typeIds);
            bool workshared = doc.IsWorkshared;
            WorksetTable? worksetTable = workshared ? doc.GetWorksetTable() : null;

            if (fids.Count == 0 && tids.Count == 0)
                return new FamilyInstancesResult(0, workshared, 0, 0, new List<FamilyInstanceDetail>());

            IEnumerable<FamilyInstance> query = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol?.Family is { } fam
                             && (fids.Count == 0 || fids.Contains(fam.Id.Value))
                             && (tids.Count == 0 || tids.Contains(fi.Symbol.Id.Value)));

            List<FamilyInstanceDetail> all = query
                .Select(fi => new FamilyInstanceDetail(
                    fi.Id.Value,
                    fi.Symbol?.Name ?? string.Empty,
                    fi.Symbol?.Id.Value ?? 0,
                    fi.Category?.Name ?? string.Empty,
                    (doc.GetElement(fi.LevelId) as Level)?.Name ?? string.Empty,
                    workshared ? fi.WorksetId.IntegerValue : 0,
                    workshared && worksetTable is not null
                        ? worksetTable.GetWorkset(fi.WorksetId)?.Name ?? string.Empty
                        : string.Empty))
                .OrderBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = all.Count;
            List<FamilyInstanceDetail> rows =
                limit is int lim and > 0 && all.Count > lim ? all.Take(lim).ToList() : all;
            long primary = fids.Count == 1 ? fids.First() : 0;
            return new FamilyInstancesResult(primary, workshared, total, rows.Count, rows);
        }

        /// <summary>
        /// One row per family TYPE (FamilySymbol) across the given families: family/type/category, placed
        /// instance count and the distinct worksets its instances sit on. Backs the Family Types tab (the
        /// UI groups these rows by type name). Deliberately lightweight — type parameters are NOT included
        /// (there can be hundreds and they bloat/slow the payload).
        /// </summary>
        public TypeRowsResult GetFamilyTypeRows(Document doc, IEnumerable<long> familyIds)
        {
            HashSet<long> ids = new(familyIds);
            bool workshared = doc.IsWorkshared;
            WorksetTable? worksetTable = workshared ? doc.GetWorksetTable() : null;

            // One pass over instances → per-symbol instance count and the distinct workset names.
            Dictionary<long, int> counts = new();
            Dictionary<long, HashSet<string>> worksetsBySymbol = new();
            foreach (FamilyInstance fi in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                if (fi.Symbol?.Family is not { } fam || !ids.Contains(fam.Id.Value)) continue;
                long sid = fi.Symbol.Id.Value;
                counts.TryGetValue(sid, out int c);
                counts[sid] = c + 1;
                if (workshared && worksetTable is not null)
                {
                    string ws = worksetTable.GetWorkset(fi.WorksetId)?.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(ws))
                        (worksetsBySymbol.TryGetValue(sid, out HashSet<string>? set)
                            ? set
                            : worksetsBySymbol[sid] = new HashSet<string>()).Add(ws);
                }
            }

            List<TypeRow> rows = new();
            foreach (long familyId in ids)
            {
                if (doc.GetElement(new ElementId(familyId)) is not Family family) continue;
                string category = family.FamilyCategory?.Name ?? string.Empty;

                foreach (ElementId symbolId in family.GetFamilySymbolIds())
                {
                    if (doc.GetElement(symbolId) is not FamilySymbol symbol) continue;
                    long sid = symbol.Id.Value;
                    rows.Add(new TypeRow(
                        familyId,
                        family.Name,
                        sid,
                        symbol.Name,
                        category,
                        counts.TryGetValue(sid, out int n) ? n : 0,
                        worksetsBySymbol.TryGetValue(sid, out HashSet<string>? ws)
                            ? ws.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                            : new List<string>()));
                }
            }

            return new TypeRowsResult(workshared, rows.Count, rows);
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

    public sealed record WorksetInfo(
        [property: JsonProperty("id")] int Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("isOpen")] bool IsOpen,
        [property: JsonProperty("isEditable")] bool IsEditable,
        [property: JsonProperty("owner")] string Owner);

    public sealed record WorksetsResult(
        [property: JsonProperty("isWorkshared")] bool IsWorkshared,
        [property: JsonProperty("worksets")] IReadOnlyList<WorksetInfo> Worksets);

    public sealed record FamilyInstanceDetail(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("typeName")] string TypeName,
        [property: JsonProperty("typeId")] long TypeId,
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("level")] string Level,
        [property: JsonProperty("worksetId")] int WorksetId,
        [property: JsonProperty("workset")] string Workset);

    public sealed record FamilyInstancesResult(
        [property: JsonProperty("familyId")] long FamilyId,
        [property: JsonProperty("isWorkshared")] bool IsWorkshared,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("returned")] int Returned,
        [property: JsonProperty("instances")] IReadOnlyList<FamilyInstanceDetail> Instances);

    public sealed record TypeRow(
        [property: JsonProperty("familyId")] long FamilyId,
        [property: JsonProperty("familyName")] string FamilyName,
        [property: JsonProperty("typeId")] long TypeId,
        [property: JsonProperty("typeName")] string TypeName,
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("instanceCount")] int InstanceCount,
        [property: JsonProperty("worksets")] IReadOnlyList<string> Worksets);

    public sealed record TypeRowsResult(
        [property: JsonProperty("isWorkshared")] bool IsWorkshared,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("rows")] IReadOnlyList<TypeRow> Rows);
}
