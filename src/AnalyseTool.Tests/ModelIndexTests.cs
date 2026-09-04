using AnalyseTool.Core.Common.Index;
using TUnit.Core.Exceptions;

namespace AnalyseTool.Tests;

/// <summary>
/// The Revit-free half of the model index: the journal's fold (a pure function), the store's replace
/// and tombstone rules, and the query guard — the one piece where a mistake lets a language model
/// write into the index. SQLite tests run on Windows (winsqlite3.dll) and skip elsewhere; the fold
/// runs everywhere.
/// </summary>
public class ModelIndexTests
{
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("winsqlite3.dll is a Windows system library; the plugin only ever runs on Windows.");
    }

    private static ChangeBatch Batch(long[]? added = null, long[]? modified = null, long[]? deleted = null, string? transaction = null) =>
        new(added ?? Array.Empty<long>(), modified ?? Array.Empty<long>(), deleted ?? Array.Empty<long>(),
            transaction is null ? Array.Empty<string>() : new[] { transaction }, DateTime.UtcNow);

    [Test]
    public async Task The_fold_reads_an_element_once_however_often_it_changed()
    {
        ChangeSet set = ChangeJournal.Coalesce(new[]
        {
            Batch(added: new long[] { 1 }, transaction: "Create"),
            Batch(modified: new long[] { 1, 2 }, transaction: "Move"),
            Batch(modified: new long[] { 1 }, transaction: "Move"),
        }, overflowed: false);

        await Assert.That(set.ToRead).IsEquivalentTo(new long[] { 1, 2 });
        await Assert.That(set.ToDelete).IsEmpty();
        await Assert.That(set.Transactions).IsEquivalentTo(new[] { "Create", "Move" });
        await Assert.That(set.Batches).IsEqualTo(3);
    }

    [Test]
    public async Task The_fold_lets_the_last_word_win()
    {
        // Added then deleted: never read. Deleted then added again (an undo): read, not deleted.
        ChangeSet set = ChangeJournal.Coalesce(new[]
        {
            Batch(added: new long[] { 1, 2 }),
            Batch(deleted: new long[] { 1, 3 }),
            Batch(added: new long[] { 3 }),
        }, overflowed: false);

        await Assert.That(set.ToRead).IsEquivalentTo(new long[] { 2, 3 });
        await Assert.That(set.ToDelete).IsEquivalentTo(new long[] { 1 });
    }

    [Test]
    public async Task An_overflowed_journal_asks_for_a_reconcile_instead_of_a_replay()
    {
        ChangeJournal journal = new();
        journal.Record(Batch(added: new long[] { 1 }));
        journal.Record(Batch(modified: Enumerable.Range(0, ChangeJournal.Capacity).Select(i => (long)i).ToArray()));

        await Assert.That(journal.HasPending).IsTrue();
        ChangeSet set = journal.Drain();
        await Assert.That(set.Overflowed).IsTrue();
        await Assert.That(set.IsEmpty).IsFalse();
        await Assert.That(journal.HasPending).IsFalse();
    }

    [Test]
    public async Task A_tombstone_keeps_the_element_row_and_drops_its_values()
    {
        RequireWindows();
        using ModelIndexStore store = ModelIndexStore.CreateInMemory();
        ParameterDef comment = new(-1010, "Kommentare", "ALL_MODEL_INSTANCE_COMMENTS", null, "String", "string", null, false);
        store.Write(new[]
        {
            new ElementRead(Row("a", 1), new[] { comment }, new[] { new ParameterValueRow(1, -1010, "x", null, null) }),
            new ElementRead(Row("b", 2), Array.Empty<ParameterDef>(), new[] { new ParameterValueRow(2, -1010, "y", null, null) }),
        });

        store.Tombstone(new long[] { 1 });

        await Assert.That(store.Count("elements")).IsEqualTo(2);
        await Assert.That(store.LiveElements).IsEqualTo(1);
        await Assert.That(store.Scalar<long>("SELECT COUNT(*) FROM v_elements WHERE element_id = 1")).IsEqualTo(0);
        await Assert.That(store.Scalar<string>("SELECT deleted_at FROM elements WHERE unique_id = 'a'")).IsNotNull();
        await Assert.That(store.Scalar<long>("SELECT COUNT(*) FROM parameter_values WHERE element_id = 1")).IsEqualTo(0);
        await Assert.That(store.Scalar<long>("SELECT COUNT(*) FROM parameter_values WHERE element_id = 2")).IsEqualTo(1);
        await Assert.That(store.LiveVersions().Keys).IsEquivalentTo(new long[] { 2 });
    }

    [Test]
    public async Task Re_reading_an_element_drops_a_parameter_it_no_longer_has()
    {
        RequireWindows();
        using ModelIndexStore store = ModelIndexStore.CreateInMemory();
        ParameterDef a = new(-1, "A", null, null, "String", "string", null, false);
        ParameterDef b = new(-2, "B", null, null, "String", "string", null, false);
        store.Write(new[] { new ElementRead(Row("u", 7), new[] { a, b }, new[] { new ParameterValueRow(7, -1, "1", null, null), new ParameterValueRow(7, -2, "2", null, null) }) });
        store.Write(new[] { new ElementRead(Row("u", 7) with { VersionGuid = "v2" }, Array.Empty<ParameterDef>(), new[] { new ParameterValueRow(7, -1, "1b", null, null) }) });

        await Assert.That(store.Scalar<long>("SELECT COUNT(*) FROM parameter_values WHERE element_id = 7")).IsEqualTo(1);
        await Assert.That(store.Scalar<string>("SELECT value_text FROM parameter_values WHERE element_id = 7")).IsEqualTo("1b");
        await Assert.That(store.LiveVersions()[7]).IsEqualTo("v2");
    }

    [Test]
    public async Task Open_recreates_a_file_whose_schema_version_is_not_ours()
    {
        RequireWindows();
        string path = Path.Combine(Path.GetTempPath(), "analysetool-index-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (ModelIndexStore first = ModelIndexStore.Open(path, out bool created))
            {
                await Assert.That(created).IsTrue();
                first.Write(new[] { new ElementRead(Row("a", 1), Array.Empty<ParameterDef>(), Array.Empty<ParameterValueRow>()) });
                first.SetMeta("schema_version", "0");
            }
            using (ModelIndexStore second = ModelIndexStore.Open(path, out bool created))
            {
                await Assert.That(created).IsTrue();
                await Assert.That(second.Count("elements")).IsEqualTo(0);
                await Assert.That(second.GetMeta("schema_version")).IsEqualTo(ModelIndexStore.SchemaVersion);
            }
            using (ModelIndexStore third = ModelIndexStore.Open(path, out bool created))
            {
                await Assert.That(created).IsFalse();
                await Assert.That(third.JournalMode).IsEqualTo("wal");
            }
        }
        finally { ModelIndexStore.DeleteFiles(path); }
    }

    [Test]
    public async Task The_query_guard_lets_selects_through_and_refuses_everything_else()
    {
        RequireWindows();
        string path = Path.Combine(Path.GetTempPath(), "analysetool-index-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (ModelIndexStore store = ModelIndexStore.Create(path))
            {
                store.Write(Enumerable.Range(1, 5).Select(i =>
                    new ElementRead(Row($"u{i}", i), Array.Empty<ParameterDef>(), Array.Empty<ParameterValueRow>())).ToList());
                store.Checkpoint();
            }

            QueryResult ok = IndexQuery.Execute(path, "SELECT element_id, name FROM v_elements ORDER BY element_id", 2, CancellationToken.None);
            await Assert.That(ok.Error).IsNull();
            await Assert.That(ok.Columns).IsEquivalentTo(new[] { "element_id", "name" });
            await Assert.That(ok.RowCount).IsEqualTo(2);
            await Assert.That(ok.Truncated).IsTrue();
            await Assert.That(ok.Rows[0][0]).IsEqualTo(1L);

            QueryResult cte = IndexQuery.Execute(path, "WITH w AS (SELECT COUNT(*) AS n FROM v_elements) SELECT n FROM w", null, CancellationToken.None);
            await Assert.That(cte.Error).IsNull();
            await Assert.That(cte.Rows[0][0]).IsEqualTo(5L);

            foreach (string forbidden in new[]
            {
                "DELETE FROM elements",
                "INSERT INTO meta (key, value) VALUES ('x', 'y')",
                "UPDATE elements SET name = 'x'",
                "SELECT 1; DELETE FROM elements",
                "ATTACH DATABASE ':memory:' AS other",
                "PRAGMA journal_mode = DELETE",
                "CREATE TABLE t (x)",
                "DROP VIEW v_elements",
            })
            {
                QueryResult refused = IndexQuery.Execute(path, forbidden, null, CancellationToken.None);
                await Assert.That(refused.Error).IsNotNull();
            }

            using ModelIndexStore after = ModelIndexStore.Open(path, out _);
            await Assert.That(after.Count("elements")).IsEqualTo(5);
            await Assert.That(after.Scalar<string>("SELECT name FROM elements WHERE element_id = 1")).IsEqualTo("e1");
        }
        finally { ModelIndexStore.DeleteFiles(path); }
    }

    [Test]
    public async Task A_query_against_a_missing_index_says_so_instead_of_creating_one()
    {
        RequireWindows();
        string path = Path.Combine(Path.GetTempPath(), "analysetool-index-" + Guid.NewGuid().ToString("N") + ".db");
        QueryResult result = IndexQuery.Execute(path, "SELECT 1", null, CancellationToken.None);
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(File.Exists(path)).IsFalse();
    }

    private static ElementRow Row(string uniqueId, long id) =>
        new(uniqueId, id, false, "Wände", "OST_Walls", "Model", $"e{id}", null, null, null,
            null, null, "v1", null, null, null, null, null, null, null, null, null);
}
