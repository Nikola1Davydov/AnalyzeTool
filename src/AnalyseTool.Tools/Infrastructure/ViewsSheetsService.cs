using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Infrastructure
{
    /// <summary>Collects views and sheets information from a document.</summary>
    public sealed class ViewsSheetsService
    {
        public ViewsAndSheetsResult GetViewsAndSheets(Document doc)
        {
            HashSet<ElementId> viewsOnSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => vp.ViewId)
                .ToHashSet();

            List<View> views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();

            List<ViewInfo> viewInfos = views
                .Select(v => new ViewInfo(v.Id.Value, v.Name, viewsOnSheets.Contains(v.Id)))
                .ToList();

            List<SheetInfo> sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => new SheetInfo(s.Id.Value, s.SheetNumber, s.Name))
                .ToList();

            return new ViewsAndSheetsResult(viewInfos, sheets);
        }
    }

    public sealed record ViewInfo(long Id, string Name, bool IsOnSheet);
    public sealed record SheetInfo(long Id, string Number, string Name);
    public sealed record ViewsAndSheetsResult(List<ViewInfo> Views, List<SheetInfo> Sheets);
}
