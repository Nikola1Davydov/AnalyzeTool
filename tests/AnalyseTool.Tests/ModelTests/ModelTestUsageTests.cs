using System.IO;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Testing;

namespace AnalyseTool.Tests.ModelTests;

/// <summary>The statistics of the model tests: every run counted per test, skipped ones not, kept in the profile.</summary>
public sealed class ModelTestUsageTests
{
    [Test]
    public async Task Record_CountsEveryTestThatRan_AndSurvivesAReload()
    {
        var profile = Path.Combine(Path.GetTempPath(), "AnalyseTool.Tests", "usage", Guid.NewGuid().ToString("N"));
        var paths = new ExtensionPaths(profile);
        var names = new Dictionary<string, (string, string, string)> { ["s/a"] = ("A", "Suite", "ext"), ["s/b"] = ("B", "Suite", "ext") };
        var store = new ModelTestUsageStore(paths);

        store.Record(Run(Result("s/a", "failed", 2), Result("s/b", "skipped", 0)), names);
        store.Record(Run(Result("s/a", "error", 0), Result("s/b", "passed", 0)), names);
        var read = new ModelTestUsageStore(paths).Read();

        var a = read.Tests.Single(test => test.TestId == "s/a");
        using (Assert.Multiple())
        {
            await Assert.That(read.Runs).IsEqualTo(2);
            await Assert.That(a.Runs).IsEqualTo(2);
            await Assert.That(a.Failed).IsEqualTo(1);
            await Assert.That(a.Errors).IsEqualTo(1);
            await Assert.That(a.LastStatus).IsEqualTo("error");
            await Assert.That(a.LastError).IsEqualTo("boom");
            await Assert.That(read.Tests.Single(test => test.TestId == "s/b").Runs).IsEqualTo(1);
            await Assert.That(new ModelTestUsageStore(paths).Reset()).IsEqualTo(2);
        }
    }

    private static ModelTestRun Run(params ModelTestResult[] results)
    {
        return new ModelTestRun("Model", "key", DateTimeOffset.Now, 10, "page", true, new ModelTestTotals(results.Length, 0, 0, 0, 0, 0, 0, 0, 0), results, []);
    }

    private static ModelTestResult Result(string id, string status, int fresh)
    {
        var cases = status == "error" ? new[] { new ModelTestCase("k", "error", "boom", null, null, [], false, null, null) } : [];
        return new ModelTestResult(id, status, 1, fresh, fresh, 0, 0, 0, status == "error" ? 1 : 0, null, cases, false, 5);
    }
}
