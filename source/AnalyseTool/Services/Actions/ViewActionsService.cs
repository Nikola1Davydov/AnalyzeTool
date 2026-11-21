using System.Text.Json.Serialization;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Services.Actions;

/// <summary>The outcome of a temporary isolate in the active view.</summary>
/// <param name="Ok">Whether the view now isolates the elements.</param>
/// <param name="Isolated">How many elements were isolated.</param>
/// <param name="Error">Why nothing was isolated, if so.</param>
/// <param name="Warnings">Revit warnings the transaction resolved.</param>
public sealed record IsolationResult(bool Ok, int Isolated, string? Error, IReadOnlyList<TransactionWarning> Warnings);

/// <summary>The outcome of showing elements in a 3D view.</summary>
/// <param name="Ok">Whether the 3D view shows the elements now.</param>
/// <param name="Isolated">How many elements it shows.</param>
/// <param name="View">The name of the 3D view.</param>
/// <param name="Error">Why not, if so.</param>
public sealed record ShowIn3DResult(bool Ok, int Isolated, string? View, string? Error);

/// <summary>
///     The outcome of setting the selection. <see cref="Selected" /> is read back from Revit: ids the document does not
///     hold never make it into the selection.
/// </summary>
/// <param name="Ok">Whether the selection was set.</param>
/// <param name="Selected">How many elements Revit holds selected now.</param>
/// <param name="Error">Why the selection was not set, if so.</param>
/// <param name="IgnoredIds">The requested ids that are not in the document; absent when all were found.</param>
public sealed record SelectionResult(
    bool Ok,
    int Selected,
    string? Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<long>? IgnoredIds = null);

/// <summary>
///     The view actions of the Actions slice as functions of a document: isolating elements in the active view. Call it on
///     the Revit thread.
/// </summary>
public sealed class ViewActionsService
{
    /// <summary>How far a section box reaches beyond the elements: 300 mm, like a selection box with room to see around.</summary>
    private const double SectionBoxMargin = 300 / 304.8;

    /// <summary>
    ///     Shows the elements, selected and zoomed to: isolated temporarily in the active view — whatever view it is, the view
    ///     never changes — or, with <paramref name="sectionBox" />, inside a section box around them with their surroundings
    ///     (Revit's Selection Box), in the active view when it is 3D, else <c>{3D}</c> or any 3D view, created when there is none.
    /// </summary>
    public ShowIn3DResult ShowIn3D(Autodesk.Revit.UI.UIDocument uiDocument, IReadOnlyList<long> elementIds, bool sectionBox = false)
    {
        var document = uiDocument.Document;
        var ids = elementIds.Select(id => new ElementId(id)).Where(id => document.GetElement(id) is not null).ToList();
        if (ids.Count == 0)
        {
            return new ShowIn3DResult(false, 0, null, "None of the elements is in the model.");
        }

        if (!sectionBox)
        {
            return IsolateInActiveView(uiDocument, ids);
        }

        using var transaction = new Transaction(document, "Show in 3D");
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction);
        var view = Find3DView(document) ?? Create3DView(document);
        if (view is null)
        {
            transaction.RollBack();
            return new ShowIn3DResult(false, 0, null, "The model has no 3D view type to create a 3D view from.");
        }

        // Read before any rollback: a rollback deletes a view this transaction created, and its Name with it
        var viewName = view.Name;
        if (view.IsTemporaryHideIsolateActive())
        {
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        var box = BoxAround(document, ids);
        if (box is null)
        {
            transaction.RollBack();
            return new ShowIn3DResult(false, 0, viewName, "The elements have no geometry to put a section box around.");
        }

        view.SetSectionBox(box);
        view.IsSectionBoxActive = true;

        var rolledBack = failures.RollbackReason(transaction.Commit());
        if (rolledBack is not null)
        {
            return new ShowIn3DResult(false, 0, viewName, rolledBack);
        }

        if (uiDocument.ActiveView?.Id != view.Id)
        {
            uiDocument.ActiveView = view;
        }

        uiDocument.Selection.SetElementIds(ids);
        uiDocument.GetOpenUIViews().FirstOrDefault(candidate => candidate.ViewId == view.Id)?.ZoomAndCenterRectangle(box.Min, box.Max);

        return new ShowIn3DResult(true, ids.Count, viewName, null);
    }

    /// <summary>
    ///     Isolates the elements temporarily in the active view, the view the user looks at: a plan stays a plan. Elements the
    ///     view does not show (another level, another phase) are left out; none shown is an answer, not an empty view.
    /// </summary>
    private static ShowIn3DResult IsolateInActiveView(Autodesk.Revit.UI.UIDocument uiDocument, IReadOnlyList<ElementId> ids)
    {
        var document = uiDocument.Document;
        var view = uiDocument.ActiveView;
        if (view is null || view.IsTemplate || view.ViewType is ViewType.Schedule or ViewType.DrawingSheet or ViewType.ProjectBrowser
                or ViewType.SystemBrowser or ViewType.Internal or ViewType.Undefined or ViewType.Report or ViewType.PanelSchedule
                or ViewType.ColumnSchedule)
        {
            return new ShowIn3DResult(false, 0, view?.Name, "The active view cannot isolate elements: open a plan, a section or a 3D view.");
        }

        using var transaction = new Transaction(document, "Isolate");
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction);
        // An earlier isolation first: the view's collector sees only what it leaves visible
        if (view.IsTemporaryHideIsolateActive())
        {
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        var wanted = ids.Select(id => id.Value).ToHashSet();
        var shown = new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType().ToElementIds()
            .Where(id => wanted.Contains(id.Value)).ToList();
        if (shown.Count == 0)
        {
            // Say why when the view hides their category: «Doors are off in Visibility/Graphics» is a fix, «not shown» a riddle
            var hidden = ids.Select(document.GetElement).Select(element => element?.Category)
                .Where(category => category is not null && view.GetCategoryHidden(category.Id)).Select(category => category!.Name).Distinct().ToList();
            transaction.RollBack();
            var why = hidden.Count > 0 ? $" — {string.Join(", ", hidden)} {(hidden.Count == 1 ? "is" : "are")} hidden in its Visibility/Graphics" : "";
            return new ShowIn3DResult(false, 0, view.Name,
                $"The active view «{view.Name}» does not show {(ids.Count == 1 ? "the element" : "any of the elements")}{why}: open a view where {(ids.Count == 1 ? "it is" : "they are")}, or use Selection box.");
        }

        view.IsolateElementsTemporary(shown);
        var rolledBack = failures.RollbackReason(transaction.Commit());
        if (rolledBack is not null)
        {
            return new ShowIn3DResult(false, 0, view.Name, rolledBack);
        }

        uiDocument.Selection.SetElementIds(shown);
        // The isolated view shows only the elements: zoom to fit is zoom to them
        uiDocument.GetOpenUIViews().FirstOrDefault(candidate => candidate.ViewId == view.Id)?.ZoomToFit();
        return new ShowIn3DResult(true, shown.Count, view.Name, shown.Count < ids.Count ? $"{ids.Count - shown.Count} of the elements are not in this view." : null);
    }

    private static IEnumerable<XYZ> Corners(XYZ min, XYZ max)
    {
        foreach (var x in new[] { min.X, max.X })
        foreach (var y in new[] { min.Y, max.Y })
        foreach (var z in new[] { min.Z, max.Z })
        {
            yield return new XYZ(x, y, z);
        }
    }

    /// <summary>The box around the elements in model coordinates, with a margin; <see langword="null" /> when none has geometry.</summary>
    private static BoundingBoxXYZ? BoxAround(Document document, IEnumerable<ElementId> ids)
    {
        XYZ? min = null;
        XYZ? max = null;
        foreach (var id in ids)
        {
            if (document.GetElement(id)?.get_BoundingBox(null) is not { } box)
            {
                continue;
            }

            // A box of an element may carry a transform: its corners, moved into the model
            foreach (var corner in Corners(box.Min, box.Max).Select(box.Transform.OfPoint))
            {
                min = min is null ? corner : new XYZ(Math.Min(min.X, corner.X), Math.Min(min.Y, corner.Y), Math.Min(min.Z, corner.Z));
                max = max is null ? corner : new XYZ(Math.Max(max.X, corner.X), Math.Max(max.Y, corner.Y), Math.Max(max.Z, corner.Z));
            }
        }

        if (min is null || max is null)
        {
            return null;
        }

        var margin = new XYZ(SectionBoxMargin, SectionBoxMargin, SectionBoxMargin);
        return new BoundingBoxXYZ { Min = min - margin, Max = max + margin };
    }

    /// <summary>
    ///     A 3D view AnalyseTool may change: the active one, else {3D}, else any — never one a view template governs (its
    ///     section box and visibility belong to the template, and Revit refuses to change them).
    /// </summary>
    private static View3D? Find3DView(Document document)
    {
        static bool Free(View3D view) => !view.IsTemplate && view.CanBePrinted && view.ViewTemplateId == ElementId.InvalidElementId;

        if (document.ActiveView is View3D active && Free(active))
        {
            return active;
        }

        var views = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().Where(Free).ToList();
        return views.FirstOrDefault(view => view.Name is "{3D}" || view.Name.StartsWith("{3D", StringComparison.Ordinal)) ?? views.FirstOrDefault(view => !view.IsPerspective);
    }

    private static View3D? Create3DView(Document document)
    {
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(candidate => candidate.ViewFamily == ViewFamily.ThreeDimensional);
        return type is null ? null : View3D.CreateIsometric(document, type.Id);
    }


    /// <summary>Isolates the elements temporarily in the active view of <paramref name="document" />, in one transaction.</summary>
    public IsolationResult Isolate(Document document, IReadOnlyList<long> elementIds)
    {
        if (elementIds.Count == 0)
        {
            return new IsolationResult(true, 0, null, []);
        }

        var ids = elementIds.Select(id => new ElementId(id)).ToList();
        var view = document.ActiveView;
        if (view is null || !view.IsModifiable)
        {
            return new IsolationResult(false, 0, "The active view cannot be modified.", []);
        }

        using var transaction = new Transaction(document, "Isolate");
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction);

        if (view.IsTemporaryHideIsolateActive())
        {
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        view.IsolateElementsTemporary(ids);
        var rolledBack = failures.RollbackReason(transaction.Commit());
        return rolledBack is null
            ? new IsolationResult(true, ids.Count, null, failures.Warnings)
            : new IsolationResult(false, 0, rolledBack, failures.Warnings);
    }
}
