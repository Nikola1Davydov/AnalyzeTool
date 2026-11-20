using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Extensions.Testing;

/// <summary>What a repair did.</summary>
/// <param name="TestId">The test.</param>
/// <param name="Fixed">How many elements were repaired.</param>
/// <param name="Problems">Why the others were not.</param>
public sealed record ModelTestFixResult(string TestId, int Fixed, IReadOnlyList<string> Problems);

/// <summary>What a change of the baseline did.</summary>
/// <param name="TestId">The test.</param>
/// <param name="Accepted">How many accepted failures the test has now on this model.</param>
/// <param name="File">The file that holds them.</param>
public sealed record ModelTestBaselineResult(string TestId, int Accepted, string File);

/// <summary>
///     Runs the model tests on the active document, repairs failing elements, keeps the baseline and the trend. The pages and
///     the MCP tools in <c>Features/ModelTestCommands</c> are thin wrappers over it.
/// </summary>
public sealed class ModelTestService(ModelTestCatalog catalog, ExtensionPaths paths, IRevitDispatcher dispatcher, ModelTestUsageStore usage)
{
    private const int TrendLength = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private ModelTestRun? _last;

    /// <summary>Raised after a run: pages show it, whoever started it.</summary>
    public event EventHandler<ModelTestRun>? Completed;

    /// <summary>Gets the catalog of suites.</summary>
    public ModelTestCatalog Catalog => catalog;

    /// <summary>Gets the latest run of this session; <see langword="null" /> before the first.</summary>
    public ModelTestRun? LastRun => _last;

    /// <summary>Runs the tests the options pick on the active document; the model is never changed.</summary>
    public async Task<ModelTestRun> RunAsync(ModelTestRunOptions options, string trigger, CancellationToken cancellationToken)
    {
        var tests = Select(catalog.Suites(), options);
        var history = HistoryDirectory;
        var run = await dispatcher.InvokeAsync(application =>
        {
            var document = ActiveDocument(application);
            using var group = new TransactionGroup(document, "AnalyseTool: model tests");
            // A read-only document cannot be changed anyway; the group makes sure a test that tries leaves no trace
            var started = !document.IsReadOnly && !document.IsModifiable && group.Start() == TransactionStatus.Started;
            try
            {
                return Execute(new RevitModelAccess(document), tests, options, trigger, history, cancellationToken);
            }
            finally
            {
                if (started)
                {
                    group.RollBack();
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        _last = run;
        // The statistics: every run counts, whoever started it
        usage.Record(run, tests.ToDictionary(pair => pair.Test.Id, pair => (pair.Test.Name, pair.Suite.DisplayName, pair.Suite.ExtensionId), StringComparer.OrdinalIgnoreCase));
        Completed?.Invoke(this, run);
        return run;
    }

    /// <summary>Runs the repair of a test on failing elements, in one transaction (one Ctrl+Z).</summary>
    /// <param name="testId">The test.</param>
    /// <param name="keys">The cases to repair; every new failure, found afresh, when absent.</param>
    /// <param name="cancellationToken">Stops between elements.</param>
    public Task<ModelTestFixResult> FixAsync(string testId, IReadOnlyList<string>? keys, CancellationToken cancellationToken)
    {
        var (suite, test) = Find(testId);
        if (!test.HasFix)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"{testId} has no repair: add .Fix(...) or .FixValue(...) to it.");
        }

        return dispatcher.InvokeAsync(application =>
        {
            var document = ActiveDocument(application);
            var access = new RevitModelAccess(document);
            var chosen = keys is { Count: > 0 }
                ? keys
                : RunAlone(document, access, suite, test, cancellationToken).Cases.Where(item => item.Status == "failed" && !item.InBaseline).Select(item => item.Key).ToList();
            using var transaction = new Transaction(document, $"AnalyseTool: {test.FixLabel}");
            transaction.Start();
            var (fixedCount, problems) = ModelTestEngine.Fix(test, access, chosen, cancellationToken);
            if (fixedCount > 0)
            {
                transaction.Commit();
            }
            else
            {
                transaction.RollBack();
            }

            return new ModelTestFixResult(testId, fixedCount, problems);
        }, cancellationToken);
    }

    /// <summary>Accepts failures of a test on the active model, or takes them back.</summary>
    /// <param name="testId">The test.</param>
    /// <param name="keys">The cases; every failure, found afresh, when absent.</param>
    /// <param name="remove">Takes the cases out of the baseline instead (all of them when no keys are given).</param>
    /// <param name="cancellationToken">Cancels the wait for Revit.</param>
    public async Task<ModelTestBaselineResult> AcceptAsync(string testId, IReadOnlyList<string>? keys, bool remove, CancellationToken cancellationToken)
    {
        var (suite, test) = Find(testId);
        var (modelKey, title, fresh) = await dispatcher.InvokeAsync(application =>
        {
            var document = ActiveDocument(application);
            var access = new RevitModelAccess(document);
            return (access.ModelKey, access.ModelTitle, remove ? null : RunAlone(document, access, suite, test, cancellationToken));
        }, cancellationToken).ConfigureAwait(false);

        var count = ChangeBaseline(suite, test, modelKey, title, fresh, keys, remove);
        return new ModelTestBaselineResult(testId, count, ModelTestBaseline.PathOf(suite.BaselineDirectory, modelKey));
    }

    /// <summary>
    ///     The run itself, without Revit: picks the baseline of every suite, runs the tests, files the counts of a full run in
    ///     the trend. The tests call it with a fake model.
    /// </summary>
    internal static ModelTestRun Execute(IModelAccess access, IReadOnlyList<(SuiteDefinition Suite, TestDefinition Test)> tests, ModelTestRunOptions options, string trigger, string? historyDirectory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var modelKey = access.ModelKey;
        var baselines = new Dictionary<string, BaselineFile>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ModelTestResult>();
        foreach (var (suite, test) in tests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!baselines.TryGetValue(suite.Id, out var baseline))
            {
                baseline = ModelTestBaseline.Load(suite.BaselineDirectory, modelKey);
                baselines[suite.Id] = baseline;
            }

            results.Add(ModelTestEngine.Run(test, access, ModelTestBaseline.Accepted(baseline, test.LocalId!), options, cancellationToken));
        }

        var totals = new ModelTestTotals(
            results.Count,
            results.Count(result => result.Status == "passed"),
            results.Count(result => result.Status == "failed"),
            results.Count(result => result.Status == "error"),
            results.Count(result => result.Status == "inconclusive"),
            results.Count(result => result.Status == "skipped"),
            results.Sum(result => result.New),
            results.Sum(result => result.Baseline),
            results.Sum(result => result.Inconclusive));
        var at = DateTimeOffset.Now;
        var full = options.IsFull;
        var trend = historyDirectory is null ? [] : Trend(historyDirectory, modelKey, full ? new ModelTestTrendPoint(at, totals.NewCases, totals.BaselineCases, totals.Failed) : null);
        return new ModelTestRun(access.ModelTitle, modelKey, at, stopwatch.ElapsedMilliseconds, trigger, full, totals, results, trend);
    }

    /// <summary>Picks the tests of a run, in the order of the suites.</summary>
    internal static List<(SuiteDefinition Suite, TestDefinition Test)> Select(IReadOnlyList<SuiteDefinition> suites, ModelTestRunOptions options)
    {
        var wanted = options.Tests is { Count: > 0 } ids ? ids.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var trait = options.Trait?.Split('=', 2);
        return suites
            .Where(suite => options.Suite is null || string.Equals(suite.Id, options.Suite, StringComparison.OrdinalIgnoreCase) || string.Equals(suite.ExtensionId, options.Suite, StringComparison.OrdinalIgnoreCase))
            .SelectMany(suite => suite.Tests.Select(test => (Suite: suite, Test: test)))
            .Where(pair => wanted is null || wanted.Contains(pair.Test.Id))
            .Where(pair => trait is null || pair.Test.Traits.Any(item => string.Equals(item.Key, trait[0].Trim(), StringComparison.OrdinalIgnoreCase)
                                                                         && (trait.Length == 1 || string.Equals(item.Value, trait[1].Trim(), StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    /// <summary>Accepts or takes back cases; the cases to accept come from a result of the test.</summary>
    internal static int ChangeBaseline(SuiteDefinition suite, TestDefinition test, string modelKey, string modelTitle, ModelTestResult? latest, IReadOnlyList<string>? keys, bool remove)
    {
        var chosen = keys is { Count: > 0 } ? keys.ToHashSet(StringComparer.Ordinal) : null;
        if (remove)
        {
            var current = ModelTestBaseline.Accepted(ModelTestBaseline.Load(suite.BaselineDirectory, modelKey), test.LocalId!);
            return ModelTestBaseline.Change(suite.BaselineDirectory, modelKey, modelTitle, test.LocalId!, [], chosen ?? current.ToHashSet(StringComparer.Ordinal), ExtensionManager.Author);
        }

        if (latest is null)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"There is no result of {test.Id} to accept failures from.");
        }

        var accept = latest.Cases.Where(item => item.Status == "failed" && (chosen is null || chosen.Contains(item.Key))).ToList();
        return ModelTestBaseline.Change(suite.BaselineDirectory, modelKey, modelTitle, test.LocalId!, accept, [], ExtensionManager.Author);
    }

    private string HistoryDirectory => Path.Combine(paths.ProfileRoot, "model-tests", "history");

    private (SuiteDefinition Suite, TestDefinition Test) Find(string testId)
    {
        return catalog.FindTest(testId) ?? throw new CommandException(ExtensionErrorCodes.TestNotFound, $"There is no model test '{testId}'. GetModelTests lists them.");
    }

    /// <summary>One test on every element with every case listed, in a group that is rolled back: what a repair or an accept works on.</summary>
    private static ModelTestResult RunAlone(Document document, RevitModelAccess access, SuiteDefinition suite, TestDefinition test, CancellationToken cancellationToken)
    {
        using var group = new TransactionGroup(document, "AnalyseTool: model test");
        var started = !document.IsReadOnly && !document.IsModifiable && group.Start() == TransactionStatus.Started;
        try
        {
            var accepted = ModelTestBaseline.Accepted(ModelTestBaseline.Load(suite.BaselineDirectory, access.ModelKey), test.LocalId!);
            return ModelTestEngine.Run(test, access, accepted, new ModelTestRunOptions(Tests: [test.Id], IncludeSlow: true, MaxCases: int.MaxValue), cancellationToken);
        }
        finally
        {
            if (started)
            {
                group.RollBack();
            }
        }
    }

    private static Document ActiveDocument(UIApplication application)
    {
        return application.ActiveUIDocument?.Document ?? throw new CommandException(ExtensionErrorCodes.NoDocument, "Open a model in Revit first.");
    }

    /// <summary>The counts of the earlier full runs of the model, with this one added when it is full.</summary>
    private static IReadOnlyList<ModelTestTrendPoint> Trend(string directory, string modelKey, ModelTestTrendPoint? current)
    {
        var path = Path.Combine(directory, string.Concat(modelKey.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)) + ".json");
        List<ModelTestTrendPoint> points;
        try
        {
            points = File.Exists(path) ? JsonSerializer.Deserialize<List<ModelTestTrendPoint>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            points = [];
        }

        if (current is not null)
        {
            points.Add(current);
            points = points.TakeLast(TrendLength).ToList();
            try
            {
                AtomicFile.WriteAllText(path, JsonSerializer.Serialize(points, Json));
            }
            catch (IOException)
            {
                // The trend is a nicety: a held file costs a point, not the run
            }
        }

        return points;
    }
}
