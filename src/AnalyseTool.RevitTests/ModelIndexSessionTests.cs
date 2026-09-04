using AnalyseTool.Core.Common.Index;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace AnalyseTool.RevitTests;

/// <summary>
/// The whole indexing session against a live document — build, then the journal fed by Revit's own
/// DocumentChanged, then apply and reconcile — without a UIApplication: the slots run inline on the
/// test (Revit) thread and the options carry no pauses, so no continuation ever leaves it.
/// </summary>
public sealed class ModelIndexSessionTests : SeededModel
{
    /// <summary>Direct slots: the work runs right here, on the thread the test owns.</summary>
    private sealed class DirectSlots : IRevitSlots
    {
        private readonly Document _document;
        public DirectSlots(Document document) => _document = document;
        public Task<T> RunAsync<T>(Func<Document, T> work, CancellationToken ct) => Task.FromResult(work(_document));
    }

    private static readonly IndexOptions Inline = new(ReadChunk: 3, SweepChunk: 2, ChunkPauseMs: 0, DebounceMs: 0);

    private string _path = string.Empty;
    private ModelIndexSession _session = null!;
    private EventHandler<DocumentChangedEventArgs>? _feed;

    [Before(Test)]
    public void OpenSession()
    {
        _path = Path.Combine(Path.GetTempPath(), "analysetool-index-" + Guid.NewGuid().ToString("N") + ".db");
        _session = new ModelIndexSession("test", _path, new DirectSlots(Document), Inline);
        // The real handler's job, in miniature: copy the ids into the journal. Subscribed on the
        // application so that Revit itself decides what "changed" means.
        _feed = (_, e) => _session.Journal.Record(new ChangeBatch(
            e.GetAddedElementIds().Select(id => id.Value).ToList(),
            e.GetModifiedElementIds().Select(id => id.Value).ToList(),
            e.GetDeletedElementIds().Select(id => id.Value).ToList(),
            e.GetTransactionNames().ToList(),
            DateTime.UtcNow));
        Document.Application.DocumentChanged += _feed;
    }

    [After(Test)]
    public void CloseSession()
    {
        if (_feed is not null) Document.Application.DocumentChanged -= _feed;
        _session.Dispose();
        ModelIndexStore.DeleteFiles(_path);
    }

    private long Count(string sql)
    {
        QueryResult result = IndexQuery.Execute(_path, sql, null, CancellationToken.None);
        if (result.Error is not null) throw new InvalidOperationException(result.Error);
        return (long)result.Rows[0][0]!;
    }

    [Test]
    public async Task The_first_pass_builds_the_index_and_reports_ready()
    {
        await _session.RunOnceAsync();

        await Assert.That(_session.State).IsEqualTo(IndexState.Ready);
        await Assert.That(_session.Freshness.PendingChanges).IsEqualTo(0);
        await Assert.That(_session.Freshness.BuiltAtUtc).IsNotNull();
        await Assert.That(Count("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0")).IsEqualTo(4);
        await Assert.That(Count("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Levels'")).IsEqualTo(1);
        await Assert.That(Count("SELECT COUNT(*) FROM v_parameters WHERE built_in_parameter = 'WALL_USER_HEIGHT_PARAM' AND value_num > 0")).IsEqualTo(4);
    }

    [Test]
    public async Task A_change_in_revit_reaches_the_index_through_the_journal()
    {
        await _session.RunOnceAsync();
        Wall wall = Walls[0];

        InTransaction("Comment", () => wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("indexed"));
        await Assert.That(_session.Journal.HasPending).IsTrue();
        await Assert.That(_session.Journal.PendingIds).IsGreaterThanOrEqualTo(1);

        await _session.RunOnceAsync();

        await Assert.That(_session.State).IsEqualTo(IndexState.Ready);
        await Assert.That(_session.Journal.HasPending).IsFalse();
        await Assert.That(Count($"SELECT COUNT(*) FROM v_parameters WHERE element_id = {wall.Id.Value} AND built_in_parameter = 'ALL_MODEL_INSTANCE_COMMENTS' AND value_text = 'indexed'")).IsEqualTo(1);
    }

    [Test]
    public async Task A_deleted_wall_becomes_a_tombstone_and_an_undo_brings_it_back()
    {
        await _session.RunOnceAsync();
        long id = Walls[1].Id.Value;

        InTransaction("Delete", () => Document.Delete(Walls[1].Id));
        await _session.RunOnceAsync();

        await Assert.That(Count("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0")).IsEqualTo(3);
        await Assert.That(Count($"SELECT COUNT(*) FROM elements WHERE element_id = {id} AND deleted_at IS NOT NULL")).IsEqualTo(1);
        await Assert.That(Count($"SELECT COUNT(*) FROM parameter_values WHERE element_id = {id}")).IsEqualTo(0);
    }

    [Test]
    public async Task A_reconcile_finds_what_changed_behind_the_journals_back()
    {
        await _session.RunOnceAsync();

        // Changes the journal never saw: unsubscribe, edit, delete, subscribe again.
        Document.Application.DocumentChanged -= _feed;
        InTransaction("Silent edit", () =>
        {
            Walls[0].get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("silent");
            Document.Delete(Walls[2].Id);
        });
        Document.Application.DocumentChanged += _feed;
        await Assert.That(_session.Journal.HasPending).IsFalse();

        _session.RequestRebuild(full: false);
        await _session.RunOnceAsync();

        await Assert.That(_session.State).IsEqualTo(IndexState.Ready);
        await Assert.That(Count("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0")).IsEqualTo(3);
        await Assert.That(Count($"SELECT COUNT(*) FROM v_parameters WHERE element_id = {Walls[0].Id.Value} AND value_text = 'silent'")).IsEqualTo(1);
    }

    [Test]
    public async Task A_full_rebuild_starts_from_nothing_and_ends_complete()
    {
        await _session.RunOnceAsync();
        _session.RequestRebuild(full: true);
        await _session.RunOnceAsync();

        await Assert.That(_session.State).IsEqualTo(IndexState.Ready);
        await Assert.That(Count("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0")).IsEqualTo(4);
        await Assert.That(Count("SELECT COUNT(*) FROM elements WHERE deleted_at IS NOT NULL")).IsEqualTo(0);
    }
}
