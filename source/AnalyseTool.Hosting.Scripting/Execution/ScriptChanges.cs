using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;

namespace AnalyseTool.Hosting.Scripting.Execution;

/// <summary>
///     What a script did to the model: the elements it added, modified and deleted.
/// </summary>
/// <param name="Added">How many elements were added.</param>
/// <param name="Modified">How many existing elements were modified.</param>
/// <param name="Deleted">How many elements were deleted.</param>
/// <param name="AddedIds">The first <see cref="ChangeRecorder.IdLimit" /> added ids.</param>
/// <param name="ModifiedIds">The first modified ids.</param>
/// <param name="DeletedIds">The first deleted ids.</param>
internal sealed record ScriptChanges(
    int Added,
    int Modified,
    int Deleted,
    IReadOnlyList<long> AddedIds,
    IReadOnlyList<long> ModifiedIds,
    IReadOnlyList<long> DeletedIds)
{
    public static ScriptChanges None { get; } = new(0, 0, 0, [], [], []);
}

/// <summary>
///     Collects the changes of one document from <see cref="Application.DocumentChanged" /> while a script runs.
/// </summary>
/// <remarks>
///     Revit raises the event when a transaction commits, also inside a transaction group, so a dry run sees what it would
///     change. Recording stops before the group ends: its rollback raises the event again.
/// </remarks>
internal sealed class ChangeRecorder : IDisposable
{
    /// <summary>How many ids of each kind the summary lists; the counts are complete.</summary>
    public const int IdLimit = 100;

    private readonly Application _application;
    private readonly Document _document;
    private readonly HashSet<long> _added = [];
    private readonly HashSet<long> _modified = [];
    private readonly HashSet<long> _deleted = [];
    private bool _stopped;

    public ChangeRecorder(Application application, Document document)
    {
        _application = application;
        _document = document;
        _application.DocumentChanged += OnDocumentChanged;
    }

    /// <summary>Stops recording and returns what was recorded.</summary>
    public ScriptChanges Stop()
    {
        _stopped = true;
        var modified = _modified.Where(id => !_added.Contains(id) && !_deleted.Contains(id)).ToList();
        var deleted = _deleted.Where(id => !_added.Contains(id)).ToList();
        var added = _added.Where(id => !_deleted.Contains(id)).ToList();

        return new ScriptChanges(
            added.Count,
            modified.Count,
            deleted.Count,
            added.Take(IdLimit).ToList(),
            modified.Take(IdLimit).ToList(),
            deleted.Take(IdLimit).ToList());
    }

    public void Dispose()
    {
        _application.DocumentChanged -= OnDocumentChanged;
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (_stopped || !args.GetDocument().Equals(_document))
        {
            return;
        }

        foreach (var id in args.GetAddedElementIds())
        {
            _added.Add(id.Value);
        }

        foreach (var id in args.GetModifiedElementIds())
        {
            _modified.Add(id.Value);
        }

        foreach (var id in args.GetDeletedElementIds())
        {
            _deleted.Add(id.Value);
        }
    }
}
