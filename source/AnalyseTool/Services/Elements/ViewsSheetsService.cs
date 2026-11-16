namespace AnalyseTool.Services.Elements;

/// <summary>A view of the project browser.</summary>
/// <param name="Id">The view id.</param>
/// <param name="Name">The view name.</param>
/// <param name="ViewType">The <c>ViewType</c> name.</param>
/// <param name="IsOnSheet">Whether a viewport places it on a sheet.</param>
public sealed record ViewInfo(long Id, string Name, string ViewType, bool IsOnSheet);

/// <summary>A sheet.</summary>
/// <param name="Id">The sheet id.</param>
/// <param name="Number">The sheet number.</param>
/// <param name="Name">The sheet name.</param>
public sealed record SheetInfo(long Id, string Number, string Name);

/// <summary>The views and sheets of a document.</summary>
/// <param name="Views">The views: no templates, sheets or browser pseudo-views.</param>
/// <param name="Sheets">The sheets.</param>
public sealed record ViewsAndSheetsResult(IReadOnlyList<ViewInfo> Views, IReadOnlyList<SheetInfo> Sheets);

/// <summary>
///     Collects the views and sheets of a document. Call it on the Revit thread.
/// </summary>
public sealed class ViewsSheetsService
{
    /// <summary>The views and sheets of <paramref name="document" />.</summary>
    public ViewsAndSheetsResult GetViewsAndSheets(Document document)
    {
        var viewsOnSheets = new FilteredElementCollector(document)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Select(viewport => viewport.ViewId)
            .ToHashSet();

        // OfClass(View) also yields the sheets and Revit's browser pseudo-views, which no person would list as views
        var views = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate && view is not ViewSheet && IsRealView(view.ViewType))
            .Select(view => new ViewInfo(view.Id.Value, view.Name, view.ViewType.ToString(), viewsOnSheets.Contains(view.Id)))
            .ToList();

        var sheets = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet => new SheetInfo(sheet.Id.Value, sheet.SheetNumber, sheet.Name))
            .ToList();

        return new ViewsAndSheetsResult(views, sheets);
    }

    private static bool IsRealView(ViewType type)
    {
        return type is not (ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Internal or ViewType.Undefined);
    }
}
