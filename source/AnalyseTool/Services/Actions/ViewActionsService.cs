using System.Text.Json.Serialization;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Services.Actions;

/// <summary>The outcome of a temporary isolate in the active view.</summary>
/// <param name="Ok">Whether the view now isolates the elements.</param>
/// <param name="Isolated">How many elements were isolated.</param>
/// <param name="Error">Why nothing was isolated, if so.</param>
/// <param name="Warnings">Revit warnings the transaction resolved.</param>
public sealed record IsolationResult(bool Ok, int Isolated, string? Error, IReadOnlyList<TransactionWarning> Warnings);

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
