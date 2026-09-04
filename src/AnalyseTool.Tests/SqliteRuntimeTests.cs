using AnalyseTool.Core.Common.Index;
using TUnit.Core.Exceptions;

namespace AnalyseTool.Tests;

/// <summary>
/// The model index runs on the sqlite3 that Windows ships (winsqlite3.dll), bound through
/// SQLitePCLRaw's provider — no native library of our own in the Revit process. These tests are the
/// Revit-free half of the phase-0 spike: they prove the binding loads, say which SQLite it is, and
/// exercise the v1 schema and its views on an in-memory database with hand-made rows. CI runs on
/// Windows (windows-2022), where the library exists; anywhere else the tests skip rather than fail,
/// because the platform itself never runs there.
/// </summary>
public class SqliteRuntimeTests
{
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("winsqlite3.dll is a Windows system library; the plugin only ever runs on Windows.");
    }

    [Test]
    public async Task Windows_provides_a_recent_sqlite_with_json()
    {
        RequireWindows();

        SqliteRuntimeInfo info = SqliteRuntime.Describe();

        // 3.31 (2020) is a floor, not a wish: below it, generated columns and the newer JSON functions
        // the index leans on are missing. Windows 10 21H2 already ships well above it.
        await Assert.That(Version.Parse(info.Version) >= new Version(3, 31)).IsTrue();
        await Assert.That(info.Json).IsTrue();
        await Assert.That(info.Provider).IsEqualTo("SQLite3Provider_winsqlite3");
    }

    [Test]
    public async Task The_schema_creates_and_the_views_join_values_to_their_definitions()
    {
        RequireWindows();

        using IndexSpikeStore store = IndexSpikeStore.CreateInMemory();

        ElementRow wall = new("uid-1", 1001, false, "Wände", "OST_Walls", "Model", "Basiswand", "Basiswand", "Generic 200", 42,
            10, null, Guid.Empty.ToString(), 0, 0, 0, -1, -1, 0, 1, 1, 3);
        ParameterDef height = new(-1001, "Unverbundene Höhe", "WALL_USER_HEIGHT_PARAM", null, "Double", "length", "millimeters", false);
        ParameterDef comment = new(-1010, "Kommentare", "ALL_MODEL_INSTANCE_COMMENTS", null, "String", "string", null, false);
        store.Write(new[]
        {
            new ElementRead(wall, new[] { height, comment }, new[]
            {
                new ParameterValueRow(1001, -1001, "4000", 4000, null),
                new ParameterValueRow(1001, -1010, null, null, null), // present and empty: a row, not an absence
            }),
        });

        await Assert.That(store.Count("elements")).IsEqualTo(1);
        await Assert.That(store.Count("parameter_defs")).IsEqualTo(2);
        await Assert.That(store.Count("parameter_values")).IsEqualTo(2);

        double? tall = store.Scalar<double?>(
            "SELECT value_num FROM v_parameters WHERE element_id = 1001 AND built_in_parameter = 'WALL_USER_HEIGHT_PARAM'");
        await Assert.That(tall).IsEqualTo(4000d);

        string? unit = store.Scalar<string>("SELECT unit FROM v_parameters WHERE name = 'Unverbundene Höhe'");
        await Assert.That(unit).IsEqualTo("millimeters");

        long empties = store.Scalar<long>("SELECT n FROM v_distribution WHERE parameter = 'Kommentare' AND value IS NULL");
        await Assert.That(empties).IsEqualTo(1);
    }

    [Test]
    public async Task A_second_write_of_the_same_element_replaces_its_rows()
    {
        RequireWindows();

        using IndexSpikeStore store = IndexSpikeStore.CreateInMemory();
        ElementRow before = new("uid-1", 1001, false, "Wände", "OST_Walls", "Model", "old", null, null, null,
            null, null, "v1", null, null, null, null, null, null, null, null, null);
        ElementRow after = before with { Name = "new", VersionGuid = "v2" };
        ParameterDef comment = new(-1010, "Kommentare", "ALL_MODEL_INSTANCE_COMMENTS", null, "String", "string", null, false);

        store.Write(new[] { new ElementRead(before, new[] { comment }, new[] { new ParameterValueRow(1001, -1010, "a", null, null) }) });
        store.Write(new[] { new ElementRead(after, Array.Empty<ParameterDef>(), new[] { new ParameterValueRow(1001, -1010, "b", null, null) }) });

        await Assert.That(store.Count("elements")).IsEqualTo(1);
        await Assert.That(store.Scalar<string>("SELECT name FROM elements WHERE unique_id = 'uid-1'")).IsEqualTo("new");
        await Assert.That(store.Scalar<string>("SELECT value_text FROM parameter_values WHERE element_id = 1001")).IsEqualTo("b");
    }
}
