using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>Collects linked content (Revit links + linked CAD files) from a document.</summary>
    public sealed class LinksService
    {
        public LinksResult GetLinks(Document doc)
        {
            List<LinkInfo> revitLinks = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(l => new LinkInfo(l.Id.Value, l.Name))
                .ToList();

            List<LinkInfo> cadLinks = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(i => i.IsLinked)
                .Select(i => new LinkInfo(i.Id.Value, i.Name))
                .ToList();

            return new LinksResult(revitLinks, cadLinks);
        }
    }

    // camelCase spelled out so the published OutputType schema matches what Newtonsoft actually writes.
    public sealed record LinkInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name);

    public sealed record LinksResult(
        [property: JsonProperty("revitLinks")] List<LinkInfo> RevitLinks,
        [property: JsonProperty("cadLinks")] List<LinkInfo> CadLinks);
}
