using System.IO;
using AnalyseTool.Services.SharedParameters;

namespace AnalyseTool.Tests.SharedParameters;

/// <summary>
///     The report's saved parameter sets: a missing file is «no sets», a broken one is reported rather than silently
///     emptied, a save keeps the previous file, and a file of 1.x reads back.
/// </summary>
public sealed class ParameterSetStoreTests
{
    private string _directory = null!;
    private string _path = null!;

    [Before(Test)]
    public void CreatePaths()
    {
        _directory = Path.Combine(Path.GetTempPath(), "at-sets-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_directory, "sets.json");
    }

    [After(Test)]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public async Task Load_NoFile_NoSets()
    {
        var result = new ParameterSetStore(_path).Load();

        await Assert.That(result.Sets).IsEmpty();
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Save_Sets_ComeBackInOrderWithIds()
    {
        var store = new ParameterSetStore(_path);
        store.Save([
            new ParameterSetDto
            {
                Name = " Fire safety ",
                Parameters =
                [
                    new ParameterRefDto { Guid = "6b1b5b1e-0000-4000-8000-000000000001", Name = "Fire rating" },
                    new ParameterRefDto { Name = "Project note" }
                ]
            }
        ]);

        var set = store.Load().Sets.Single();

        await Assert.That(set.Name).IsEqualTo("Fire safety");
        await Assert.That(set.Id).IsNotEmpty();
        await Assert.That(set.Parameters.Select(parameter => parameter.Name)).IsEquivalentTo(["Fire rating", "Project note"]);
    }

    [Test]
    public async Task Save_ProjectSet_KeepsItsProjectAndAGlobalOneWritesNone()
    {
        var store = new ParameterSetStore(_path);
        store.Save([
            new ParameterSetDto { Name = "Snowdon only", ProjectId = "c0ffee00-0000-4000-8000-000000000001", ProjectName = "Snowdon Towers" },
            new ParameterSetDto { Name = "Everywhere" }
        ]);

        var back = store.Load().Sets;

        await Assert.That(back[0].ProjectId).IsEqualTo("c0ffee00-0000-4000-8000-000000000001");
        await Assert.That(back[0].ProjectName).IsEqualTo("Snowdon Towers");
        await Assert.That(back[1].ProjectId).IsNull();
        await Assert.That(File.ReadAllText(_path)).DoesNotContain("projectId\": null");
    }

    [Test]
    public async Task Save_Twice_KeepsThePreviousFileAsBak()
    {
        var store = new ParameterSetStore(_path);
        store.Save([new ParameterSetDto { Name = "A" }]);
        store.Save([new ParameterSetDto { Name = "B" }]);

        await Assert.That(File.Exists(_path + ".bak")).IsTrue();
        await Assert.That(store.Load().Sets.Single().Name).IsEqualTo("B");
    }

    [Test]
    public async Task Save_SetWithoutName_IsDropped()
    {
        var result = new ParameterSetStore(_path).Save([new ParameterSetDto { Name = "  " }, new ParameterSetDto { Name = "Kept" }]);

        await Assert.That(result.Sets.Select(set => set.Name)).IsEquivalentTo(["Kept"]);
    }

    [Test]
    public async Task Load_BrokenFile_IsReportedNotEmptied()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, "{ not json");

        var result = new ParameterSetStore(_path).Load();

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(File.ReadAllText(_path)).IsEqualTo("{ not json");
    }

    [Test]
    public async Task Load_FileOf1x_ReadsBack()
    {
        Directory.CreateDirectory(_directory);
        // As Newtonsoft wrote it in 1.x: camelCase, indented, projectId only when set
        File.WriteAllText(_path, """
                                 [
                                   {
                                     "id": "a1",
                                     "name": "Doors handover",
                                     "parameters": [
                                       { "guid": "6b1b5b1e-0000-4000-8000-000000000001", "name": "Fire rating" },
                                       { "name": "Project note" }
                                     ],
                                     "updated": "2026-10-01T10:00:00Z"
                                   }
                                 ]
                                 """);

        var set = new ParameterSetStore(_path).Load().Sets.Single();

        await Assert.That(set.Id).IsEqualTo("a1");
        await Assert.That(set.Parameters[0].Guid).IsEqualTo("6b1b5b1e-0000-4000-8000-000000000001");
        await Assert.That(set.Parameters[1].Guid).IsNull();
        await Assert.That(set.Updated).IsEqualTo("2026-10-01T10:00:00Z");
    }
}
