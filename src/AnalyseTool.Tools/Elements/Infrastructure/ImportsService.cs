using Autodesk.Revit.DB;
using Newtonsoft.Json;

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

    // camelCase spelled out: the wire is written by Newtonsoft (declared names by default) while the
    // schema published for OutputType is generated with Web defaults (camelCase). Without the attribute
    // the two disagree, and a schema that misnames what it describes is worse than none.
    public sealed record ImportInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name);
}
