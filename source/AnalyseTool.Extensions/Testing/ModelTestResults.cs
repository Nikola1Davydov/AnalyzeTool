namespace AnalyseTool.Extensions.Testing;

/// <summary>A category or a parameter: as this Revit names it, and its API name.</summary>
/// <param name="Name">As Revit shows it, in its language: <c>Wände</c>, <c>Kennzeichen</c>.</param>
/// <param name="Api">The API name: <c>OST_Walls</c>, <c>ALL_MODEL_MARK</c>; for a parameter looked up by name, the name.</param>
public sealed record RevitName(string Name, string Api);

/// <summary>A test as the page and AI clients list it.</summary>
/// <param name="Id">The full id: <c>&lt;suite id&gt;/&lt;test id&gt;</c>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Description">What it checks: <c>Doors · Fire Rating · not empty</c>.</param>
/// <param name="Kind"><c>Rule</c>, <c>Check</c> or <c>Model</c>.</param>
/// <param name="Severity"><c>Info</c>, <c>Warning</c> or <c>Error</c>.</param>
/// <param name="Traits">Labels for filtering, <c>name=value</c>.</param>
/// <param name="Slow">Runs only when slow tests are asked for.</param>
/// <param name="Skip">Why it does not run; <see langword="null" /> when it runs.</param>
/// <param name="File">Where it is written, relative to the extension folder.</param>
/// <param name="Line">The line, from 1; 0 when unknown.</param>
/// <param name="Fix">The label of its repair; <see langword="null" /> without one.</param>
/// <param name="Categories">The categories it runs over; empty for a class or the whole model.</param>
/// <param name="ElementClass">The element class it runs over (<c>Room</c>); <see langword="null" /> for categories.</param>
/// <param name="Parameter">The parameter a rule checks; <see langword="null" /> for code or another value.</param>
/// <param name="TypeParameter">The parameter is one of the element's type.</param>
/// <param name="Value">What a rule checks when it is not a parameter: <c>Type name</c>, a label of the author.</param>
/// <param name="Checks">The conditions of a rule: <c>not empty, unique</c>.</param>
/// <param name="Filtered">The elements are filtered further in code (<c>Where</c>).</param>
public sealed record ModelTestInfo(
    string Id,
    string Name,
    string Description,
    string Kind,
    string Severity,
    IReadOnlyList<string> Traits,
    bool Slow,
    string? Skip,
    string File,
    int Line,
    string? Fix,
    IReadOnlyList<RevitName> Categories,
    string? ElementClass,
    RevitName? Parameter,
    bool TypeParameter,
    string? Value,
    string? Checks,
    bool Filtered);

/// <summary>A suite of tests.</summary>
/// <param name="Id">The id: <c>&lt;extension id&gt;.&lt;class&gt;</c>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="ExtensionId">The extension it comes from: its versions are the suite's versions.</param>
/// <param name="ExtensionName">The name of that extension: the folder the Tests tab shows the suite in.</param>
/// <param name="Directory">The extension folder: the files of the tests are relative to it.</param>
/// <param name="Tests">Its tests in the order declared.</param>
public sealed record ModelTestSuiteInfo(string Id, string Name, string ExtensionId, string ExtensionName, string Directory, IReadOnlyList<ModelTestInfo> Tests);

/// <summary>A failing, broken or undecided case of a test: one element, or the model.</summary>
/// <param name="Key">Identifies the case across runs: what the baseline keeps.</param>
/// <param name="Status"><c>failed</c>, <c>error</c> (the test threw) or <c>inconclusive</c>.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="ElementId">The element; <see langword="null" /> for a case of the whole model.</param>
/// <param name="Label">The element for people: category, name, type.</param>
/// <param name="Related">Other elements involved.</param>
/// <param name="InBaseline">The failure was accepted: it does not fail the test.</param>
/// <param name="Point">A point to show, [x, y, z] in feet.</param>
/// <param name="Box">A box to show, [minX, minY, minZ, maxX, maxY, maxZ] in feet.</param>
/// <param name="Value">The value a rule found on the element, as Revit shows it; <see langword="null" /> when it has none.</param>
/// <param name="ValueState">
///     <c>set</c>, <c>empty</c> or <c>missing</c> (the element has no such parameter) for a case of a rule;
///     <see langword="null" /> for code.
/// </param>
public sealed record ModelTestCase(
    string Key,
    string Status,
    string Message,
    long? ElementId,
    string? Label,
    IReadOnlyList<long> Related,
    bool InBaseline,
    double[]? Point,
    double[]? Box,
    string? Value = null,
    string? ValueState = null);

/// <summary>What one test found.</summary>
/// <param name="TestId">The test.</param>
/// <param name="Status">
///     <c>passed</c> (nothing new failed: accepted failures do not count), <c>failed</c>, <c>error</c> (the test threw),
///     <c>inconclusive</c> or <c>skipped</c>.
/// </param>
/// <param name="Checked">How many elements it looked at (1 for a test of the model).</param>
/// <param name="Failed">How many cases failed, accepted ones included.</param>
/// <param name="New">How many failed cases are not in the baseline.</param>
/// <param name="Baseline">How many failed cases are accepted.</param>
/// <param name="Resolved">How many accepted cases no longer fail: the baseline can shrink.</param>
/// <param name="Inconclusive">How many cases Revit could not tell.</param>
/// <param name="Errors">How many times the test threw.</param>
/// <param name="Skip">Why it was skipped.</param>
/// <param name="Cases">The cases that did not pass, new failures first; at most the limit of the run.</param>
/// <param name="Truncated">There were more cases than <paramref name="Cases" /> holds.</param>
/// <param name="Milliseconds">How long it took.</param>
public sealed record ModelTestResult(
    string TestId,
    string Status,
    int Checked,
    int Failed,
    int New,
    int Baseline,
    int Resolved,
    int Inconclusive,
    int Errors,
    string? Skip,
    IReadOnlyList<ModelTestCase> Cases,
    bool Truncated,
    long Milliseconds);

/// <summary>The counts of a run.</summary>
public sealed record ModelTestTotals(int Tests, int Passed, int Failed, int Errors, int Inconclusive, int Skipped, int NewCases, int BaselineCases, int InconclusiveCases);

/// <summary>A point of the trend: the counts of an earlier full run of the same model.</summary>
/// <param name="At">When it ran.</param>
/// <param name="NewCases">New failures then.</param>
/// <param name="BaselineCases">Accepted failures then.</param>
/// <param name="Failed">Failing tests then.</param>
public sealed record ModelTestTrendPoint(DateTimeOffset At, int NewCases, int BaselineCases, int Failed);

/// <summary>A run of tests on a model.</summary>
/// <param name="Model">The model's title.</param>
/// <param name="ModelKey">The key its baseline and history are filed under.</param>
/// <param name="At">When it ran.</param>
/// <param name="Milliseconds">How long it took.</param>
/// <param name="Trigger">Who started it: <c>page</c>, <c>watch</c>, <c>mcp</c>.</param>
/// <param name="Full">Every test that runs by default ran, on every element: only such runs make the trend.</param>
/// <param name="Totals">The counts.</param>
/// <param name="Results">A result per test that was asked for.</param>
/// <param name="Trend">The counts of earlier full runs, oldest first, this one last when it is full.</param>
public sealed record ModelTestRun(
    string Model,
    string ModelKey,
    DateTimeOffset At,
    long Milliseconds,
    string Trigger,
    bool Full,
    ModelTestTotals Totals,
    IReadOnlyList<ModelTestResult> Results,
    IReadOnlyList<ModelTestTrendPoint> Trend);

/// <summary>What a run takes.</summary>
/// <param name="Suite">Only this suite (its id), or the suites of this extension (its id).</param>
/// <param name="Tests">Only these tests (full ids).</param>
/// <param name="Trait">Only tests with this label, <c>name=value</c>.</param>
/// <param name="ElementIds">Only these elements: tests of the whole model do not run.</param>
/// <param name="IncludeSlow">Slow tests run too — the default: a run checks everything unless told to skip them.</param>
/// <param name="MaxCases">At most this many cases per test in the result.</param>
public sealed record ModelTestRunOptions(
    string? Suite = null,
    IReadOnlyList<string>? Tests = null,
    string? Trait = null,
    IReadOnlyList<long>? ElementIds = null,
    bool IncludeSlow = true,
    int MaxCases = 500)
{
    /// <summary>Gets a value indicating whether this is a run of every default test on every element.</summary>
    public bool IsFull => Suite is null && (Tests is null || Tests.Count == 0) && Trait is null && (ElementIds is null || ElementIds.Count == 0);
}

/// <summary>How far a run of model tests is: sent to the pages before every test and once at the end.</summary>
/// <param name="Done">How many tests have run.</param>
/// <param name="Total">How many the run has.</param>
/// <param name="TestId">The test running now; <see langword="null" /> at the end.</param>
/// <param name="Test">Its name, as Revit names its categories and parameters.</param>
/// <param name="Suite">The suite it belongs to.</param>
public sealed record ModelTestProgress(int Done, int Total, string? TestId, string? Test, string? Suite);
