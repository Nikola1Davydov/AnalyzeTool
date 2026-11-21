using System.ComponentModel;
using AnalyseTool.Extensions.Testing;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Threading;

namespace AnalyseTool.Extensions.Features;

/// <summary>The model tests there are.</summary>
/// <param name="Suites">The suites, by extension.</param>
public sealed record ModelTestList(IReadOnlyList<ModelTestSuiteInfo> Suites);

/// <summary>Lists the model tests.</summary>
[McpTool(ReadOnly = true, Title = "Model tests")]
[Command(Description = "Lists the model tests of the loaded extensions: suites (id, name, extension) and their tests — id, name, what it checks, " +
                       "kind (Rule: a declarative check of one value per element; Check: code per element; Model: code once), severity, traits, slow, skip, " +
                       "the file and line it is written at, the label of its repair, and what a rule checks: categories and parameter as Revit names them " +
                       "(name, in Revit's language) and by API name (api: OST_Walls, ALL_MODEL_MARK), the conditions. A suite is a C# class implementing IModelTestSuite in an extension; " +
                       "write one with the authoring guide (GetAuthoringGuide, section «Model tests») or CreateModelTestSuite.")]
public sealed class GetModelTests(ModelTestCatalog catalog, IRevitDispatcher dispatcher) : ICommand<ModelTestList>
{
    /// <inheritdoc />
    public async Task<ModelTestList> ExecuteAsync(CommandContext context)
    {
        // The names Revit shows come from Revit: asked on its thread
        var suites = await dispatcher.InvokeAsync(_ => catalog.GetSuites(RevitModelAccess.Names), context.CancellationToken).ConfigureAwait(false);
        return new ModelTestList(suites);
    }
}

/// <summary>Input of <see cref="RunModelTests" />.</summary>
public sealed record RunModelTestsRequest(
    [property: Description("Only this suite (its id) or the suites of this extension (its id).")] string? Suite = null,
    [property: Description("Only these tests, by full id.")] IReadOnlyList<string>? Tests = null,
    [property: Description("Only tests with this trait, 'name=value' or 'name'.")] string? Trait = null,
    [property: Description("Only these elements (ids): tests of the whole model do not run.")] IReadOnlyList<long>? ElementIds = null,
    [property: Description("Run slow tests too; a slow test named in 'tests' runs anyway.")] bool IncludeSlow = false,
    [property: Description("At most this many failing cases per test in the result; 200 by default.")] int MaxCases = 200,
    [property: Description("Who runs it, shown on the page: page, watch, mcp. Defaults to where the call comes from.")] string? Trigger = null);

/// <summary>Runs model tests.</summary>
[McpTool(ReadOnly = true, Title = "Run model tests")]
[Command(Description = "Runs model tests on the active Revit model and returns a result per test: status (passed — nothing new failed, accepted failures do not count; failed; error — the test threw; " +
                       "inconclusive; skipped), counts (checked, failed, new, baseline, resolved), and the cases that did not pass with element id, label, message and a key. " +
                       "Tests run inside a transaction group that is rolled back: the model never changes. Without filters every test that is not slow runs on every element, " +
                       "and the counts join the trend of the model.")]
public sealed class RunModelTests(ModelTestService service) : ICommand<RunModelTestsRequest, ModelTestRun>
{
    /// <inheritdoc />
    public Task<ModelTestRun> ExecuteAsync(RunModelTestsRequest request, CommandContext context)
    {
        var options = new ModelTestRunOptions(request.Suite, request.Tests, string.IsNullOrWhiteSpace(request.Trait) ? null : request.Trait, request.ElementIds, request.IncludeSlow,
            Math.Clamp(request.MaxCases, 1, 10_000));
        return service.RunAsync(options, request.Trigger ?? context.Request.Source, context.CancellationToken);
    }
}

/// <summary>Returns the latest run.</summary>
[McpTool(ReadOnly = true, Title = "Latest model test run")]
[Command(Description = "Returns the latest run of model tests in this Revit session (as RunModelTests returned it), or null before the first run.")]
public sealed class GetModelTestResults(ModelTestService service) : ICommand<ModelTestRun?>
{
    /// <inheritdoc />
    public Task<ModelTestRun?> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(service.LastRun);
    }
}

/// <summary>Input of <see cref="FixModelTestCases" />.</summary>
public sealed record FixModelTestCasesRequest(
    [property: Description("The full id of the test whose repair runs.")] string TestId,
    [property: Description("The keys of the cases to repair (from the run's result); every new failure when absent.")] IReadOnlyList<string>? Keys = null);

/// <summary>Repairs failing elements.</summary>
[McpTool(Title = "Repair failing elements")]
[Command(Description = "Runs the repair a test declares (Fix / FixValue) on its failing elements, in one transaction the user can undo with Ctrl+Z. " +
                       "Without keys it repairs every new failure (not the accepted ones). Run the tests again to see the result.")]
public sealed class FixModelTestCases(ModelTestService service) : ICommand<FixModelTestCasesRequest, ModelTestFixResult>
{
    /// <inheritdoc />
    public Task<ModelTestFixResult> ExecuteAsync(FixModelTestCasesRequest request, CommandContext context)
    {
        return service.FixAsync(request.TestId, request.Keys, context.CancellationToken);
    }
}

/// <summary>Input of <see cref="AcceptModelTestFailures" />.</summary>
public sealed record AcceptModelTestFailuresRequest(
    [property: Description("The full id of the test.")] string TestId,
    [property: Description("The keys of the cases (from the run's result); every current failure when absent.")] IReadOnlyList<string>? Keys = null,
    [property: Description("Take the cases out of the baseline instead (all of them without keys).")] bool Remove = false);

/// <summary>Accepts failures into the baseline.</summary>
[McpTool(Title = "Accept model test failures")]
[Command(Description = "Accepts failures of a test on the active model into its baseline, so only new failures fail it — the way to start testing a model that already has many. " +
                       "The baseline is a file of the suite's folder (baseline\\<model>.json), versioned with its tests. With remove it takes accepted failures back. " +
                       "Ask the user before accepting failures: it hides them from the result.")]
public sealed class AcceptModelTestFailures(ModelTestService service) : ICommand<AcceptModelTestFailuresRequest, ModelTestBaselineResult>
{
    /// <inheritdoc />
    public Task<ModelTestBaselineResult> ExecuteAsync(AcceptModelTestFailuresRequest request, CommandContext context)
    {
        return service.AcceptAsync(request.TestId, request.Keys, request.Remove, context.CancellationToken);
    }
}

/// <summary>Input of <see cref="CreateModelTestSuite" />.</summary>
public sealed record CreateModelTestSuiteRequest(
    [property: Description("The name of the suite, e.g. 'BEP Project X'.")] string Name,
    [property: Description("The id of the folder; made from the name when absent.")] string? Id = null,
    [property: Description("What the suite checks.")] string? Description = null,
    [property: Description("An existing folder (extension id) to add the suite to — a project holds many suites; a new folder when absent.")] string? Extension = null);

/// <summary>Creates a suite of model tests.</summary>
[McpTool(Title = "Create model tests")]
[Command(Description = "Creates a folder of model tests in the user's extensions — a project — with one C# suite (src\\<Name>.cs) of three example tests, compiled and loaded at once, " +
                       "with its version history; with 'extension' it adds one more suite to that folder instead. Edit it with WriteExtensionFile; every save reloads the tests. Returns the extension.")]
public sealed class CreateModelTestSuite(ExtensionManager manager) : ICommand<CreateModelTestSuiteRequest, ExtensionSummary>
{
    /// <inheritdoc />
    public async Task<ExtensionSummary> ExecuteAsync(CreateModelTestSuiteRequest request, CommandContext context)
    {
        var state = string.IsNullOrWhiteSpace(request.Extension)
            ? await manager.CreateTestSuiteAsync(request.Name, request.Id, request.Description, null, context.CancellationToken).ConfigureAwait(false)
            : await manager.AddTestSuiteAsync(request.Extension.Trim(), request.Name, context.CancellationToken).ConfigureAwait(false);
        return ExtensionSummary.From(state);
    }
}

/// <summary>How one model test has run.</summary>
/// <param name="TestId">The full id of the test.</param>
/// <param name="Test">Its name.</param>
/// <param name="Suite">The name of its suite.</param>
/// <param name="Folder">The name of its folder (extension).</param>
/// <param name="Exists">It is still declared; <see langword="false" /> for a test that is gone or renamed.</param>
/// <param name="Runs">How many times it ran.</param>
/// <param name="Failed">Runs with new failures.</param>
/// <param name="Errors">Runs in which the test threw: a bug in the test.</param>
/// <param name="Inconclusive">Runs in which Revit could not tell.</param>
/// <param name="LastStatus">The status of the last run.</param>
/// <param name="LastRun">When it ran last.</param>
/// <param name="LastNew">New failures of the last run.</param>
/// <param name="LastError">The message of the last time the test threw.</param>
/// <param name="AverageMilliseconds">The average time of a run.</param>
/// <param name="Last30Days">Runs per day, the oldest first, today last.</param>
public sealed record ModelTestStatistic(
    string TestId,
    string Test,
    string Suite,
    string Folder,
    bool Exists,
    int Runs,
    int Failed,
    int Errors,
    int Inconclusive,
    string? LastStatus,
    DateTimeOffset? LastRun,
    int LastNew,
    string? LastError,
    double? AverageMilliseconds,
    IReadOnlyList<int> Last30Days);

/// <summary>The statistics of the model tests.</summary>
/// <param name="Since">When the counting started.</param>
/// <param name="Runs">Runs of tests (one Run all is one run).</param>
/// <param name="Tests">Every test that ran, most run first.</param>
public sealed record ModelTestStatisticsResult(DateTimeOffset? Since, int Runs, IReadOnlyList<ModelTestStatistic> Tests);

/// <summary>Returns how the model tests have run.</summary>
[McpTool(ReadOnly = true, Title = "Model test statistics")]
[Command(Description = "Returns how every model test has run on this machine: runs, runs with new failures, runs in which the test threw (a bug in the test), " +
                       "the last status and time, the average run time and runs per day of the last 30 days. Counted whoever ran them: the pane, Watch, AI clients.")]
public sealed class GetModelTestStatistics(ModelTestUsageStore usage, ModelTestCatalog catalog, TimeProvider? time = null) : ICommand<ModelTestStatisticsResult>
{
    /// <inheritdoc />
    public Task<ModelTestStatisticsResult> ExecuteAsync(CommandContext context)
    {
        var snapshot = usage.Read();
        var today = DateOnly.FromDateTime((time ?? TimeProvider.System).GetLocalNow().DateTime);
        // Names as declared now; a test that is gone keeps the names it had
        var current = catalog.Suites().SelectMany(suite => suite.Tests.Select(test => (Suite: suite, Test: test)))
            .ToDictionary(pair => pair.Test.Id, StringComparer.OrdinalIgnoreCase);
        var tests = snapshot.Tests.Select(used =>
        {
            var exists = current.TryGetValue(used.TestId, out var pair);
            var days = Enumerable.Range(0, 30)
                .Select(offset => today.AddDays(offset - 29).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                .Select(day => used.Days.GetValueOrDefault(day))
                .ToList();
            // A rule without a name keeps the name of its run: Revit's names, which this thread cannot read
            return new ModelTestStatistic(used.TestId, exists && pair.Test.ExplicitName is not null ? pair.Test.Name : used.Test, exists ? pair.Suite.DisplayName : used.Suite,
                exists ? pair.Suite.ExtensionName ?? pair.Suite.ExtensionId : used.ExtensionId, exists, used.Runs, used.Failed, used.Errors, used.Inconclusive,
                used.LastStatus, used.LastRun, used.LastNew, used.LastError, used.AverageMilliseconds, days);
        }).ToList();
        return Task.FromResult(new ModelTestStatisticsResult(snapshot.Since, snapshot.Runs, tests));
    }
}

/// <summary>The result of <see cref="ResetModelTestStatistics" />.</summary>
/// <param name="Removed">How many tests were forgotten.</param>
public sealed record ResetModelTestStatisticsResult(int Removed);

/// <summary>Forgets the statistics of the model tests.</summary>
[Command(Description = "Forgets the run counts of every model test.")]
public sealed class ResetModelTestStatistics(ModelTestUsageStore usage) : ICommand<ResetModelTestStatisticsResult>
{
    /// <inheritdoc />
    public Task<ResetModelTestStatisticsResult> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new ResetModelTestStatisticsResult(usage.Reset()));
    }
}
