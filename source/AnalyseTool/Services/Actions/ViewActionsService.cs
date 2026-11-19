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
    /// <summary>
    ///     Shows the elements on their own in a 3D view — the active one when it is 3D, else <c>{3D}</c> or any 3D view, created
    ///     when there is none — isolated temporarily, selected, active and zoomed to them.
    /// </summary>
    public ShowIn3DResult ShowIn3D(Autodesk.Revit.UI.UIDocument uiDocument, IReadOnlyList<long> elementIds)
    {
        var document = uiDocument.Document;
        var ids = elementIds.Select(id => new ElementId(id)).Where(id => document.GetElement(id) is not null).ToList();
        if (ids.Count == 0)
        {
            return new ShowIn3DResult(false, 0, null, "None of the elements is in the model.");
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

        if (view.IsTemporaryHideIsolateActive())
        {
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        view.IsolateElementsTemporary(ids);
        var rolledBack = failures.RollbackReason(transaction.Commit());
        if (rolledBack is not null)
        {
            return new ShowIn3DResult(false, 0, view.Name, rolledBack);
        }

        if (uiDocument.ActiveView?.Id != view.Id)
        {
            uiDocument.ActiveView = view;
        }

        uiDocument.Selection.SetElementIds(ids);
        // The isolated view shows only the elements: zoom to fit is zoom to them
        uiDocument.GetOpenUIViews().FirstOrDefault(open => open.ViewId == view.Id)?.ZoomToFit();
        return new ShowIn3DResult(true, ids.Count, view.Name, null);
    }

    private static View3D? Find3DView(Document document)
    {
        if (document.ActiveView is View3D { IsTemplate: false } active && active.CanBePrinted)
        {
            return active;
        }

        var views = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().Where(view => !view.IsTemplate && view.CanBePrinted).ToList();
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
