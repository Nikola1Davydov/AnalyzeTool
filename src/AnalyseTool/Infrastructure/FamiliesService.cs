using Autodesk.Revit.DB;

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
    }

    public sealed record FamilyInstanceInfo(long Id, string Name, string Category);
}
