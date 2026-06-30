using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Infrastructure
{
    /// <summary>
    /// Model-write operations behind Family Control's actions: delete (families/types — also the Purge
    /// path), rename and workset reassignment. Every operation runs in its own transaction with a
    /// warning-swallowing failure handler (see <see cref="SwallowWarningsPreprocessor"/>) so a Revit
    /// warning never pops a modal dialog on the Revit thread.
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
                using Transaction t = new(doc, "Family Control: delete");
                t.Start();
                SwallowWarningsPreprocessor.Apply(t);
                ICollection<ElementId> deleted = doc.Delete(ids);
                t.Commit();
                return new DeleteResult(true, ids.Count, deleted?.Count ?? 0, null);
            }
            catch (Exception ex)
            {
                return new DeleteResult(false, 0, 0, ex.Message);
            }
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
                using Transaction t = new(doc, "Family Control: rename");
                t.Start();
                SwallowWarningsPreprocessor.Apply(t);
                element.Name = newName.Trim();
                t.Commit();
                return new RenameResult(true, element.Name, null);
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
                using Transaction t = new(doc, "Family Control: set workset");
                t.Start();
                SwallowWarningsPreprocessor.Apply(t);
                foreach (ElementId id in ids)
                {
                    Parameter? p = doc.GetElement(id)?.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (p is { IsReadOnly: false } && p.Set(worksetId)) updated++;
                }
                t.Commit();
                return new WorksetAssignResult(true, updated, null);
            }
            catch (Exception ex)
            {
                return new WorksetAssignResult(false, 0, ex.Message);
            }
        }
    }

    public sealed record DeleteResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("requested")] int Requested,
        [property: JsonProperty("deleted")] int Deleted,
        [property: JsonProperty("error")] string? Error);

    public sealed record RenameResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("name")] string? Name,
        [property: JsonProperty("error")] string? Error);

    public sealed record WorksetAssignResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("updated")] int Updated,
        [property: JsonProperty("error")] string? Error);
}
