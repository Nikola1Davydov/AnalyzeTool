using AnalyseTool.Sdk;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Model-write operations behind Family Control's actions: delete (families/types — also the Purge
    /// path), rename and workset reassignment. Every operation runs in its own transaction with a
    /// <see cref="CollectingFailuresPreprocessor"/>, so a Revit warning never pops a modal dialog on the
    /// Revit thread — and, unlike the swallowing handler this slice used before, the warnings it resolved
    /// come back in the result instead of vanishing. These are the commands a batch runs unattended;
    /// silently discarded warnings there leave no witness at all.
    /// </summary>
    public sealed class FamilyActionsService
    {
        /// <summary>
        /// Deletes the given families and/or types. Deleting a family removes all its instances; deleting
        /// a type removes that type's instances (and the family if it was the last type). Covers both the
        /// single "Delete" action and "Purge unused" (caller passes the unused ids it computed).
        /// </summary>
        public DeleteResult DeleteFamilyElements(Document doc, IList<long>? familyIds, IList<long>? typeIds)
        {
            HashSet<ElementId> ids = new();
            foreach (long id in familyIds ?? new List<long>())
                if (doc.GetElement(new ElementId(id)) is Family) ids.Add(new ElementId(id));
            foreach (long id in typeIds ?? new List<long>())
                if (doc.GetElement(new ElementId(id)) is ElementType) ids.Add(new ElementId(id));

            if (ids.Count == 0) return new DeleteResult(true, 0, 0, null);

            try
            {
                using Transaction t = new(doc, "Family Manager: delete");
                t.Start();
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(t);
                ICollection<ElementId> deleted = doc.Delete(ids);
                t.Commit();
                return new DeleteResult(true, ids.Count, deleted?.Count ?? 0, null, failures.Warnings);
            }
            catch (Exception ex)
            {
                return new DeleteResult(false, 0, 0, ex.Message);
            }
        }

        /// <summary>
        /// Decides which of the requested type ids to actually attempt to delete for "purge unused". The
        /// family itself is never deleted and at least one type is always kept: if EVERY type of a loadable
        /// family is unused, all but one are planned (one is left in place). Partial sets and system types
        /// are planned as-is. Returned to the caller so deletion can be chunked (for live progress) while
        /// this whole-family reasoning stays a single, consistent decision.
        /// </summary>
        public List<long> PlanPurgeTypes(Document doc, IList<long>? typeIds)
        {
            List<ElementId> ids = (typeIds ?? new List<long>())
                .Select(id => new ElementId(id))
                .Where(eid => doc.GetElement(eid) is ElementType)
                .ToList();

            Dictionary<ElementId, List<ElementId>> perFamily = new();
            List<ElementId> plan = new();
            foreach (ElementId id in ids)
            {
                if (doc.GetElement(id) is FamilySymbol { Family: { } fam })
                {
                    if (!perFamily.TryGetValue(fam.Id, out List<ElementId>? list))
                        perFamily[fam.Id] = list = new List<ElementId>();
                    list.Add(id);
                }
                else
                {
                    plan.Add(id); // system ElementType (no owning Family)
                }
            }

            foreach (KeyValuePair<ElementId, List<ElementId>> entry in perFamily)
            {
                int total = (doc.GetElement(entry.Key) as Family)?.GetFamilySymbolIds().Count ?? entry.Value.Count;
                // All types unused → keep one (Skip 1); otherwise the used types remain, delete all requested.
                plan.AddRange(entry.Value.Count >= total ? entry.Value.Skip(1) : entry.Value);
            }

            return plan.Select(eid => eid.Value).ToList();
        }

        /// <summary>Validates the given ids as families for "purge unused families" — families are deleted
        /// whole (no keep-one), so the plan is simply the ids that really are <see cref="Family"/>. Deletion
        /// then goes through <see cref="PurgeChunk"/>, chunked for live progress.</summary>
        public List<long> PlanPurgeFamilies(Document doc, IList<long>? familyIds) =>
            (familyIds ?? new List<long>())
                .Where(id => doc.GetElement(new ElementId(id)) is Family)
                .ToList();

        /// <summary>
        /// Deletes one chunk of planned ids. Each element is deleted in its OWN top-level transaction
        /// inside a <see cref="TransactionGroup"/>: a failure rolls back only that transaction and the rest
        /// still delete. (A single batch delete aborts everything on the first failure; catching a delete
        /// exception inside a sub-transaction and continuing is unsafe and can crash Revit — hence isolated
        /// transactions.) Chunking exists so the caller can yield the Revit/UI thread between chunks and
        /// report live progress; the trade-off is one undo entry per chunk. Returns per-chunk counts.
        /// </summary>
        public ChunkResult PurgeChunk(
            Document doc, IList<long> chunkIds, string what = "type", string whatPlural = "types")
        {
            List<ElementId> ids = chunkIds.Select(id => new ElementId(id)).ToList();
            int deleted = 0, failed = 0;
            // One preprocessor per transaction, so the warnings are gathered across the whole chunk —
            // a purge of hundreds of types would otherwise report only whatever the last one raised.
            List<TransactionWarning> warnings = new();

            using TransactionGroup group = new(doc, $"Family Manager: purge unused {whatPlural}");
            group.Start();

            foreach (ElementId id in ids)
            {
                if (doc.GetElement(id) is null) continue; // already removed by a previous cascade

                // Named after what is actually being deleted. Both purge commands share this method, and
                // the name reaches two places that matter: Revit's undo stack, and the log line the
                // failure preprocessor writes. A run that purges types and then families produced 300+
                // warnings all labelled "Purge type", so which of the two destructive nodes raised them
                // could not be told apart afterwards.
                using Transaction t = new(doc, $"Purge {what}");
                t.Start();
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(t);
                try
                {
                    doc.Delete(id);
                    t.Commit();
                }
                catch
                {
                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                }
                warnings.AddRange(failures.Warnings);
            }

            group.Assimilate();

            deleted = ids.Count(eid => doc.GetElement(eid) is null);
            failed = ids.Count - deleted;
            return new ChunkResult(deleted, failed, warnings);
        }

        /// <summary>Renames a family. Fails (ok=false) on a duplicate or invalid name rather than throwing.</summary>
        public RenameResult RenameFamily(Document doc, long familyId, string newName) =>
            Rename(doc, new ElementId(familyId), newName, e => e is Family);

        /// <summary>Renames a family type — a loadable FamilySymbol or a system ElementType (wall, floor,
        /// pipe…). Fails on a duplicate or invalid name.</summary>
        public RenameResult RenameFamilyType(Document doc, long typeId, string newName) =>
            Rename(doc, new ElementId(typeId), newName, e => e is ElementType);

        private static RenameResult Rename(Document doc, ElementId id, string newName, Func<Element, bool> isExpected)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return new RenameResult(false, null, "Name must not be empty.");

            Element? element = doc.GetElement(id);
            if (element is null || !isExpected(element))
                return new RenameResult(false, null, "Element not found.");

            try
            {
                using Transaction t = new(doc, "Family Manager: rename");
                t.Start();
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(t);
                element.Name = newName.Trim();
                t.Commit();
                return new RenameResult(true, element.Name, null, failures.Warnings);
            }
            catch (Exception ex)
            {
                return new RenameResult(false, null, ex.Message);
            }
        }

        /// <summary>
        /// Reassigns family instances to a target workset by setting ELEM_PARTITION_PARAM. Targets are the
        /// explicit <paramref name="elementIds"/> plus every instance of the given <paramref name="typeIds"/>
        /// (resolved here, so a grouped type row can move all its instances). No-op on a non-workshared project.
        /// </summary>
        public WorksetAssignResult SetInstancesWorkset(
            Document doc, IList<long>? elementIds, IList<long>? typeIds, int worksetId)
        {
            if (!doc.IsWorkshared)
                return new WorksetAssignResult(false, 0, "Document is not workshared.");

            HashSet<ElementId> ids = new();
            foreach (long x in elementIds ?? new List<long>())
                if (doc.GetElement(new ElementId(x)) is not null) ids.Add(new ElementId(x));

            HashSet<long> tids = new(typeIds ?? new List<long>());
            if (tids.Count > 0)
                foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    ElementId tid = e.GetTypeId();
                    if (tid is not null && tid != ElementId.InvalidElementId && tids.Contains(tid.Value))
                        ids.Add(e.Id);
                }

            if (ids.Count == 0) return new WorksetAssignResult(true, 0, null);

            try
            {
                int updated = 0;
                using Transaction t = new(doc, "Family Manager: set workset");
                t.Start();
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(t);
                foreach (ElementId id in ids)
                {
                    Parameter? p = doc.GetElement(id)?.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (p is { IsReadOnly: false } && p.Set(worksetId)) updated++;
                }
                t.Commit();
                return new WorksetAssignResult(true, updated, null, failures.Warnings);
            }
            catch (Exception ex)
            {
                return new WorksetAssignResult(false, 0, ex.Message);
            }
        }
    }

    // Warnings is last and defaulted on purpose: the early-return paths (nothing to do, validation
    // failed) never opened a transaction and have nothing to report, and a default keeps them — and
    // any other existing caller — compiling unchanged.

    public sealed record DeleteResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("requested")] int Requested,
        [property: JsonProperty("deleted")] int Deleted,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning>? Warnings = null);

    /// <summary>Per-chunk purge counts and the warnings resolved across the chunk
    /// (see <see cref="FamilyActionsService.PurgeChunk"/>).</summary>
    public sealed record ChunkResult(int Deleted, int Failed, IReadOnlyList<TransactionWarning>? Warnings = null);

    public sealed record RenameResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("name")] string? Name,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning>? Warnings = null);

    public sealed record WorksetAssignResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("updated")] int Updated,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("warnings")] IReadOnlyList<TransactionWarning>? Warnings = null);
}
