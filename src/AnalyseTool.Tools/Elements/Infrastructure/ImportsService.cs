using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>Collects imported (non-linked) CAD content from a document.</summary>
    public sealed class ImportsService
    {
        public List<ImportInfo> GetCadImports(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(i => !i.IsLinked)
                .Select(i => new ImportInfo(i.Id.Value, i.Name))
                .ToList();
    }

    public sealed record ImportInfo(long Id, string Name);
}
