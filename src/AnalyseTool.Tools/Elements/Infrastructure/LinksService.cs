using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Infrastructure
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

    public sealed record LinkInfo(long Id, string Name);
    public sealed record LinksResult(List<LinkInfo> RevitLinks, List<LinkInfo> CadLinks);
}
