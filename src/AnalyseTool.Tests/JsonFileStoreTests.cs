using AnalyseTool.Core.Common;

namespace AnalyseTool.Tests;

/// <summary>The settings stores' shared file handling: defaults for a missing or broken file, a write
/// that never leaves half a file, and concurrent read-modify-writes that all land.</summary>
public class JsonFileStoreTests
{
    public sealed class Sample
    {
        public List<string> Items { get; set; } = new();
        public string? Name { get; set; }
    }

    private string _dir = null!;
    private string _path = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-store-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "settings.json");
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task A_missing_or_broken_file_reads_as_defaults()
    {
        JsonFileStore<Sample> store = new(_path);
        await Assert.That(store.Read().Items).IsEmpty();

        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ \"Items\": [\"a\", "); // torn
        await Assert.That(store.Read().Items).IsEmpty();
    }

    [Test]
    public async Task A_write_replaces_the_file_and_leaves_no_temp_behind()
    {
        JsonFileStore<Sample> store = new(_path);

        await Assert.That(store.Write(new Sample { Name = "one" })).IsTrue();
        await Assert.That(store.Write(new Sample { Name = "two" })).IsTrue();

        using (Assert.Multiple())
        {
            await Assert.That(store.Read().Name).IsEqualTo("two");
            await Assert.That(Directory.GetFiles(_dir)).HasCount(1);
        }
    }

    [Test]
    public async Task Concurrent_updates_all_land()
    {
        JsonFileStore<Sample> store = new(_path);

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => store.Update(s => s.Items.Add("item" + i)))));

        await Assert.That(store.Read().Items).HasCount(40);
    }
}
