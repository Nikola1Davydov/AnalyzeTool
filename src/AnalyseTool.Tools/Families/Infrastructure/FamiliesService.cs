using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Families
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

        /// <summary>
        /// Non-empty type parameters for a BATCH of types (loadable or system) in one call — feeds the
        /// naming-rule engine, which composes new type names from parameter values. Values are display
        /// strings (AsValueString), i.e. already formatted in the project's units.
        /// </summary>
        public TypeParametersResult GetTypeParameters(Document doc, IReadOnlyList<long> typeIds)
        {
            List<TypeParametersInfo> types = new();
            foreach (long id in typeIds ?? [])
            {
                if (doc.GetElement(new ElementId(id)) is not ElementType type) continue;
                // ALL parameters (empty values included): the rule builder must offer every parameter a
                // type carries — an empty value on one type doesn't make the token useless for others.
                types.Add(new TypeParametersInfo(id, ReadTypeParameters(type, includeEmpty: true)));
            }
            return new TypeParametersResult(types);
        }

        /// <summary>Parameters of a family type as plain name/value pairs. By default only non-empty
        /// ones (the detail panel); <paramref name="includeEmpty"/> returns all (the naming engine).</summary>
        private static List<FamilyParameterInfo> ReadTypeParameters(Element type, bool includeEmpty = false)
        {
            List<FamilyParameterInfo> list = new();
            foreach (Parameter p in type.Parameters)
            {
                if (p?.Definition is null) continue;
                string value = ReadParameterValue(type.Document, p);
                if (string.IsNullOrEmpty(value) && !includeEmpty) continue;
                list.Add(new FamilyParameterInfo(p.Definition.Name, value));
            }
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Reads a parameter's display value WITHOUT ever hitting the native-crash paths:
        /// Parameter.AsValueString throws an uncatchable AccessViolationException on some parameters
        /// (StorageType.None, HasValue=false, certain ElementId params) — .NET cannot catch corrupted-
        /// state exceptions, so the only defense is routing by StorageType and never calling AsValueString
        /// where it isn't defined. Doubles values keep AsValueString (project units, e.g. "1000").
        /// </summary>
        private static string ReadParameterValue(Document doc, Parameter p)
        {
            if (!p.HasValue) return string.Empty;
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? string.Empty;
                case StorageType.Integer: // incl. Yes/No — AsValueString renders "Yes"/"No"
                case StorageType.Double:  // formatted in the project's display units
                    return p.AsValueString() ?? string.Empty;
                case StorageType.ElementId:
                {
                    // AsValueString on ElementId params is one of the known crash paths — resolve the
                    // referenced element's name ourselves instead.
                    ElementId id = p.AsElementId();
                    if (id == ElementId.InvalidElementId) return string.Empty;
                    return doc.GetElement(id)?.Name ?? string.Empty;
                }
                default: // StorageType.None — nothing to read, and AsValueString here can crash Revit
                    return string.Empty;
            }
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
        /// Placed instances filtered by owning family ids and/or type ids, each with its type name,
        /// category, level and workset. When only type ids are given it collects ALL elements of those
        /// types (so it works for system families — walls/floors/… — not just FamilyInstances). Backs
        /// Select/Isolate wiring and workset reassignment. Empty when both id sets are empty.
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

            string WorksetName(WorksetId id) =>
                workshared && worksetTable is not null
                    ? worksetTable.GetWorkset(id)?.Name ?? string.Empty
                    : string.Empty;

            List<FamilyInstanceDetail> all;
            if (fids.Count == 0)
            {
                // Type-ids only → any element of those types (covers loadable AND system families).
                all = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        ElementId tid = e.GetTypeId();
                        return tid is not null && tid != ElementId.InvalidElementId && tids.Contains(tid.Value);
                    })
                    .Select(e => new FamilyInstanceDetail(
                        e.Id.Value,
                        (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? string.Empty,
                        e.GetTypeId().Value,
                        e.Category?.Name ?? string.Empty,
                        (doc.GetElement(e.LevelId) as Level)?.Name ?? string.Empty,
                        workshared ? e.WorksetId.IntegerValue : 0,
                        WorksetName(e.WorksetId)))
                    .OrderBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                all = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol?.Family is { } fam
                                 && fids.Contains(fam.Id.Value)
                                 && (tids.Count == 0 || tids.Contains(fi.Symbol.Id.Value)))
                    .Select(fi => new FamilyInstanceDetail(
                        fi.Id.Value,
                        fi.Symbol?.Name ?? string.Empty,
                        fi.Symbol?.Id.Value ?? 0,
                        fi.Category?.Name ?? string.Empty,
                        (doc.GetElement(fi.LevelId) as Level)?.Name ?? string.Empty,
                        workshared ? fi.WorksetId.IntegerValue : 0,
                        WorksetName(fi.WorksetId)))
                    .OrderBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            int total = all.Count;
            List<FamilyInstanceDetail> rows =
                limit is int lim and > 0 && all.Count > lim ? all.Take(lim).ToList() : all;
            long primary = fids.Count == 1 ? fids.First() : 0;
            return new FamilyInstancesResult(primary, workshared, total, rows.Count, rows);
        }

        /// <summary>
        /// One row per family TYPE across the given loadable/in-place families PLUS every system family
        /// type (walls, floors, pipes… — these are <see cref="ElementType"/>s with no <c>Family</c>
        /// element, grouped by <c>FamilyName</c>) when <paramref name="includeSystem"/> is set. Each row
        /// carries family/type/category, placed-instance count and the distinct worksets the instances
        /// sit on. Backs the Family Types tab. Deliberately lightweight — type parameters are NOT included.
        /// </summary>
        public TypeRowsResult GetFamilyTypeRows(Document doc, IEnumerable<long> familyIds, bool includeSystem = true)
        {
            HashSet<long> ids = new(familyIds);
            bool workshared = doc.IsWorkshared;
            WorksetTable? worksetTable = workshared ? doc.GetWorksetTable() : null;

            // One pass over placed elements → per-TYPE-id instance count and distinct workset names. Keyed
            // by GetTypeId() so it covers both loadable instances (type = FamilySymbol) and system
            // elements (type = WallType/FloorType/…).
            Dictionary<long, int> counts = new();
            Dictionary<long, HashSet<string>> worksetsByType = new();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                ElementId typeId = e.GetTypeId();
                if (typeId is null || typeId == ElementId.InvalidElementId) continue;
                long t = typeId.Value;
                counts.TryGetValue(t, out int c);
                counts[t] = c + 1;
                if (workshared && worksetTable is not null)
                {
                    string ws = worksetTable.GetWorkset(e.WorksetId)?.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(ws))
                        (worksetsByType.TryGetValue(t, out HashSet<string>? set)
                            ? set
                            : worksetsByType[t] = new HashSet<string>()).Add(ws);
                }
            }

            List<string> WorksetsOf(long t) =>
                worksetsByType.TryGetValue(t, out HashSet<string>? ws)
                    ? ws.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>();

            List<TypeRow> rows = new();

            // Loadable / in-place families.
            foreach (long familyId in ids)
            {
                if (doc.GetElement(new ElementId(familyId)) is not Family family) continue;
                string category = family.FamilyCategory?.Name ?? string.Empty;

                foreach (ElementId symbolId in family.GetFamilySymbolIds())
                {
                    if (doc.GetElement(symbolId) is not FamilySymbol symbol) continue;
                    long sid = symbol.Id.Value;
                    rows.Add(new TypeRow(familyId, family.Name, sid, symbol.Name,
                        symbol.UniqueId, symbol.VersionGuid.ToString(), category,
                        counts.TryGetValue(sid, out int n) ? n : 0, WorksetsOf(sid), false));
                }
            }

            // System family types: ElementTypes that are not FamilySymbols, grouped by FamilyName, on a
            // real model category. These have no Family element (familyId = 0).
            if (includeSystem)
            {
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsElementType())
                {
                    if (el is not ElementType et || et is FamilySymbol) continue;
                    Category? cat = et.Category;
                    if (cat is null || cat.CategoryType != CategoryType.Model) continue;
                    if (string.IsNullOrWhiteSpace(et.FamilyName)) continue;

                    long tid = et.Id.Value;
                    rows.Add(new TypeRow(0, et.FamilyName, tid, et.Name,
                        et.UniqueId, et.VersionGuid.ToString(), cat.Name,
                        counts.TryGetValue(tid, out int n) ? n : 0, WorksetsOf(tid), true));
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

    public sealed record TypeParametersInfo(
        [property: JsonProperty("typeId")] long TypeId,
        [property: JsonProperty("parameters")] IReadOnlyList<FamilyParameterInfo> Parameters);

    public sealed record TypeParametersResult(
        [property: JsonProperty("types")] IReadOnlyList<TypeParametersInfo> Types);

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
        [property: JsonProperty("uniqueId")] string UniqueId,
        [property: JsonProperty("versionGuid")] string VersionGuid,
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("instanceCount")] int InstanceCount,
        [property: JsonProperty("worksets")] IReadOnlyList<string> Worksets,
        [property: JsonProperty("isSystem")] bool IsSystem);

    public sealed record TypeRowsResult(
        [property: JsonProperty("isWorkshared")] bool IsWorkshared,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("rows")] IReadOnlyList<TypeRow> Rows);
}
