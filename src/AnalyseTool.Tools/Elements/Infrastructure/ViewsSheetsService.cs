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

            // OfClass(View) also yields the sheets (a ViewSheet is a View) and Revit's own browser
            // pseudo-views ("Projektansicht", "Systembrowser"), neither of which is a view a person would
            // list — they came back as views and were counted twice or puzzled over (field test 2026-09-02).
            List<View> views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v is not ViewSheet && IsRealView(v.ViewType))
                .ToList();

            List<ViewInfo> viewInfos = views
                .Select(v => new ViewInfo(v.Id.Value, v.Name, v.ViewType.ToString(), viewsOnSheets.Contains(v.Id)))
                .ToList();

            List<SheetInfo> sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => new SheetInfo(s.Id.Value, s.SheetNumber, s.Name))
                .ToList();

            return new ViewsAndSheetsResult(viewInfos, sheets);
        }

        private static bool IsRealView(ViewType type) => type switch
        {
            ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Internal or ViewType.Undefined => false,
            _ => true,
        };
    }

    // camelCase spelled out so the published OutputType schema matches what Newtonsoft actually writes.
    public sealed record ViewInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("viewType")] string ViewType,
        [property: JsonProperty("isOnSheet")] bool IsOnSheet);

    public sealed record SheetInfo(
        [property: JsonProperty("id")] long Id,
        [property: JsonProperty("number")] string Number,
        [property: JsonProperty("name")] string Name);

    public sealed record ViewsAndSheetsResult(
        [property: JsonProperty("views")] List<ViewInfo> Views,
        [property: JsonProperty("sheets")] List<SheetInfo> Sheets);
}
