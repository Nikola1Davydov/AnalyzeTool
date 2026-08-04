using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
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

    // camelCase spelled out so the published OutputType schema matches what Newtonsoft actually writes.
    public sealed record ViewInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("isOnSheet")] bool IsOnSheet);

    public sealed record SheetInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("number")] string Number,
        [property: JsonProperty("name")] string Name);

    public sealed record ViewsAndSheetsResult(
        [property: JsonProperty("views")] List<ViewInfo> Views,
        [property: JsonProperty("sheets")] List<SheetInfo> Sheets);
}
