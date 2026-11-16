namespace AnalyseTool.Services.Elements;

/// <summary>An imported or linked file instance.</summary>
/// <param name="Id">The instance id.</param>
/// <param name="Name">The instance name.</param>
public sealed record ImportInfo(long Id, string Name);

/// <summary>A Revit link or a linked CAD file.</summary>
/// <param name="Id">The instance id.</param>
/// <param name="Name">The instance name.</param>
public sealed record LinkInfo(long Id, string Name);

/// <summary>The linked content of a document.</summary>
/// <param name="RevitLinks">The Revit link instances.</param>
/// <param name="CadLinks">The linked (not imported) CAD instances.</param>
public sealed record LinksResult(IReadOnlyList<LinkInfo> RevitLinks, IReadOnlyList<LinkInfo> CadLinks);

/// <summary>
///     Linked and imported content of a document: Revit links, linked CAD, imported CAD. Call it on the Revit thread.
/// </summary>
public sealed class LinksService
{
    /// <summary>The Revit links and the linked CAD files.</summary>
    public LinksResult GetLinks(Document document)
    {
        var revitLinks = new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Select(link => new LinkInfo(link.Id.Value, link.Name))
            .ToList();

        var cadLinks = new FilteredElementCollector(document)
            .OfClass(typeof(ImportInstance))
            .Cast<ImportInstance>()
            .Where(import => import.IsLinked)
            .Select(import => new LinkInfo(import.Id.Value, import.Name))
            .ToList();

        return new LinksResult(revitLinks, cadLinks);
    }

    /// <summary>The imported (not linked) CAD instances.</summary>
    public IReadOnlyList<ImportInfo> GetCadImports(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ImportInstance))
            .Cast<ImportInstance>()
            .Where(import => !import.IsLinked)
            .Select(import => new ImportInfo(import.Id.Value, import.Name))
            .ToList();
    }
}
