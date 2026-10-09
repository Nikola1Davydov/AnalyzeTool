using AnalyseTool.Tools.SharedParameters;

namespace AnalyseTool.Tests;

/// <summary>The report's saved parameter sets: a missing file is "no sets", a broken one is reported
/// rather than silently emptied, and a save keeps the previous file.</summary>
public class ParameterSetStoreTests
{
    private string _dir = null!;
    private string _path = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-sets-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "sets.json");
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public async Task No_file_means_no_sets()
    {
        ParameterSetsResult result = new ParameterSetStore(_path).Load();
        await Assert.That(result.Sets).IsEmpty();
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Saved_sets_come_back_in_order_with_ids()
    {
        ParameterSetStore store = new(_path);
        store.Save([
            new ParameterSetDto
            {
                Name = " Fire safety ",
                Parameters = [new ParameterRefDto { Guid = "6b1b5b1e-0000-4000-8000-000000000001", Name = "Fire rating" },
                              new ParameterRefDto { Name = "Project note" }],
            },
        ]);

        ParameterSetsResult back = store.Load();
        ParameterSetDto set = back.Sets.Single();
        await Assert.That(set.Name).IsEqualTo("Fire safety");
        await Assert.That(set.Id).IsNotEmpty();
        await Assert.That(set.Parameters.Select(p => p.Name)).IsEquivalentTo(new[] { "Fire rating", "Project note" });
    }

    [Test]
    public async Task A_second_save_keeps_the_previous_file_as_bak()
    {
        ParameterSetStore store = new(_path);
        store.Save([new ParameterSetDto { Name = "A" }]);
        store.Save([new ParameterSetDto { Name = "B" }]);

        await Assert.That(File.Exists(_path + ".bak")).IsTrue();
        await Assert.That(store.Load().Sets.Single().Name).IsEqualTo("B");
    }

    [Test]
    public async Task A_broken_file_is_reported_not_emptied()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ not json");

        ParameterSetsResult result = new ParameterSetStore(_path).Load();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(File.ReadAllText(_path)).IsEqualTo("{ not json");
    }
}
