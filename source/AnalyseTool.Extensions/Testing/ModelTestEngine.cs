using System.Diagnostics;
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions.Testing;

/// <summary>
///     Runs declared tests over a model and turns what they found into results. It knows the model only through
///     <see cref="IModelAccess" />: in Revit the caller opens (and rolls back) the transaction group around it.
/// </summary>
internal static class ModelTestEngine
{
    private const string ModelKeyPrefix = "model";

    /// <summary>Runs one test.</summary>
    /// <param name="test">The test.</param>
    /// <param name="access">The model.</param>
    /// <param name="accepted">The keys of its accepted failures.</param>
    /// <param name="options">What the run is limited to.</param>
    /// <param name="cancellationToken">Stops between elements.</param>
    public static ModelTestResult Run(TestDefinition test, IModelAccess access, IReadOnlySet<string> accepted, ModelTestRunOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var onElements = options.ElementIds is { Count: > 0 };
        if (test.SkipReason is { } reason)
        {
            return Skipped(test, reason);
        }

        var askedFor = options.Tests?.Contains(test.Id, StringComparer.OrdinalIgnoreCase) == true;
        if (test.Slow && !options.IncludeSlow && !askedFor)
        {
            return Skipped(test, "Slow: runs when slow tests are included or the test is run on its own.");
        }

        if (test.Kind == ModelTestKind.Model && onElements)
        {
            return Skipped(test, "Checks the whole model: does not run on selected elements.");
        }

        var recorder = new CaseRecorder(access);
        var context = new ModelTestContext(access, recorder, cancellationToken);
        var checkedCount = 0;
        if (test.Kind == ModelTestKind.Model)
        {
            checkedCount = 1;
            try
            {
                test.ModelBody!(context);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                recorder.Error(null, exception);
            }
        }
        else
        {
            var values = new List<(Element Element, ModelValue Value)>();
            string? label = null;
            foreach (var element in Elements(test, access, options, recorder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;
                if (test.Kind == ModelTestKind.Check)
                {
                    recorder.Subject = element;
                    try
                    {
                        test.ElementBody!(element, context);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        recorder.Error(element, exception);
                    }
                    finally
                    {
                        recorder.Subject = null;
                    }
                }
                else
                {
                    var value = Read(test.Rule!, access, element, recorder);
                    if (value is null)
                    {
                        continue;
                    }

                    label ??= Label(test.Rule!, access);
                    var failure = test.Rule!.Conditions.Select(condition => condition.Fails(label, value)).FirstOrDefault(message => message is not null);
                    if (failure is not null)
                    {
                        recorder.Add("failed", failure, new Evidence { Element = element }, value);
                    }
                    else
                    {
                        values.Add((element, value));
                    }
                }
            }

            if (test.Rule?.Unique == true)
            {
                Duplicates(label ?? Label(test.Rule, access), values, recorder);
            }
        }

        return Summarize(test, recorder, accepted, checkedCount, !onElements, options.MaxCases, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Repairs failing elements of a test: the caller holds the transaction.</summary>
    /// <param name="test">The test whose repair runs.</param>
    /// <param name="access">The model.</param>
    /// <param name="keys">The keys of the cases to repair.</param>
    /// <param name="cancellationToken">Stops between elements.</param>
    /// <returns>How many elements were repaired, and why the others were not.</returns>
    public static (int Fixed, IReadOnlyList<string> Problems) Fix(TestDefinition test, IModelAccess access, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (!test.HasFix)
        {
            return (0, [$"{test.Id} has no repair."]);
        }

        var problems = new List<string>();
        var fixedCount = 0;
        var context = new ModelTestContext(access, new CaseRecorder(access), cancellationToken);
        foreach (var uniqueId in keys.Select(ElementOf).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = access.Find(uniqueId);
            if (element is null)
            {
                problems.Add($"{uniqueId}: the element is no longer in the model.");
                continue;
            }

            try
            {
                if (test.FixValue is { } value)
                {
                    access.Write(element, test.Rule!.Source, test.Rule.Label, value, test.Rule.BuiltIn);
                }
                else
                {
                    test.Fix!(element, context);
                }

                fixedCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                problems.Add($"{access.Describe(element).Label}: {exception.Message}");
            }
        }

        return (fixedCount, problems);
    }

    /// <summary>The unique id of the element a case key is about; <see langword="null" /> for a case of the model.</summary>
    public static string? ElementOf(string key)
    {
        var first = key.Split('|')[0];
        return first.Length == 0 || first == ModelKeyPrefix ? null : first;
    }

    private static IEnumerable<Element> Elements(TestDefinition test, IModelAccess access, ModelTestRunOptions options, CaseRecorder recorder)
    {
        var ids = options.ElementIds is { Count: > 0 } list ? list.ToHashSet() : null;
        foreach (var element in access.Collect(test.Selector!))
        {
            if (ids is not null && !ids.Contains(access.Describe(element).Id))
            {
                continue;
            }

            bool keep;
            try
            {
                keep = test.Filters.All(filter => filter(element));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                recorder.Error(element, exception, "Where");
                continue;
            }

            if (keep)
            {
                yield return element;
            }
        }
    }

    private static ModelValue? Read(ValueRuleSpec rule, IModelAccess access, Element element, CaseRecorder recorder)
    {
        try
        {
            return rule.Source == ValueSource.Custom ? ModelValue.Of(rule.Read!(element)) : access.Read(element, rule.Source, rule.Label, rule.BuiltIn);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            recorder.Error(element, exception, "Reading the value");
            return null;
        }
    }

    /// <summary>A built-in parameter by the name this Revit shows; a named one as written.</summary>
    private static string Label(ValueRuleSpec rule, IModelAccess access)
    {
        if (rule.BuiltIn is not { } builtIn)
        {
            return rule.Label;
        }

        try
        {
            return access.Label(builtIn);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return rule.Label;
        }
    }

    private static void Duplicates(string label, List<(Element Element, ModelValue Value)> values, CaseRecorder recorder)
    {
        foreach (var group in values.Where(item => !item.Value.IsBlank)
                     .GroupBy(item => item.Value.Text!.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var members = group.Select(item => item.Element).ToList();
            foreach (var (element, value) in group)
            {
                var others = members.Where(other => !ReferenceEquals(other, element)).ToList();
                recorder.Add("failed", $"«{label}» '{group.Key}' is used by {others.Count} other element{(others.Count == 1 ? "" : "s")}",
                    // The others are not part of the key: who else shares the value changes, the failure stays the same
                    new Evidence { Element = element, Related = others.Take(20).ToList(), Key = "unique" }, value, relatedInKey: false);
            }
        }
    }

    private static ModelTestResult Skipped(TestDefinition test, string reason)
    {
        return new ModelTestResult(test.Id, "skipped", 0, 0, 0, 0, 0, 0, 0, reason, [], false, 0);
    }

    private static ModelTestResult Summarize(TestDefinition test, CaseRecorder recorder, IReadOnlySet<string> accepted, int checkedCount, bool everyElement, int maxCases, long milliseconds)
    {
        var cases = recorder.Cases.Select(raw => raw with { InBaseline = raw.Status == "failed" && accepted.Contains(raw.Key) }).ToList();
        var failed = cases.Count(item => item.Status == "failed");
        var baseline = cases.Count(item => item.InBaseline);
        var newCount = failed - baseline;
        var inconclusive = cases.Count(item => item.Status == "inconclusive");
        var errors = cases.Count(item => item.Status == "error");
        // An accepted case that now errors or is inconclusive was not checked, so it is not resolved either
        var seen = cases.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        // On a part of the model an accepted case that did not fail may just not have been looked at
        var resolved = everyElement ? accepted.Count(key => !seen.Contains(key)) : 0;
        var status = newCount > 0 ? "failed" : errors > 0 ? "error" : inconclusive > 0 ? "inconclusive" : "passed";
        var ordered = cases
            .OrderBy(item => item.Status switch { "failed" when !item.InBaseline => 0, "error" => 1, "inconclusive" => 2, _ => 3 })
            .ToList();
        var limit = Math.Max(1, maxCases);
        return new ModelTestResult(test.Id, status, checkedCount, failed, newCount, baseline, resolved, inconclusive, errors, null,
            ordered.Take(limit).ToList(), ordered.Count > limit, milliseconds);
    }

    /// <summary>Collects the cases of one test as they fail.</summary>
    internal sealed class CaseRecorder(IModelAccess access)
    {
        private readonly Dictionary<Element, ElementInfo> _described = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);

        public Element? Subject { get; set; }

        public List<ModelTestCase> Cases { get; } = [];

        public IModelAccess Access => access;

        /// <param name="status">failed, error, inconclusive.</param>
        /// <param name="message">What is wrong.</param>
        /// <param name="evidence">What it points at.</param>
        /// <param name="value">The value a rule found, shown with the case.</param>
        /// <param name="relatedInKey">Whether the related elements tell this case apart (two clashes of one duct), or only point at it.</param>
        public void Add(string status, string message, Evidence? evidence, ModelValue? value = null, bool relatedInKey = true)
        {
            var element = evidence?.Element ?? Subject;
            var info = element is null ? null : Describe(element);
            var related = (evidence?.Related ?? []).Select(Describe).ToList();
            var key = string.Join('|',
                info?.UniqueId ?? ModelKeyPrefix,
                relatedInKey ? string.Join(',', related.Select(item => item.UniqueId).Order(StringComparer.Ordinal)) : "",
                evidence?.Key ?? (info is null ? message : ""));
            // One element failing a check twice: the second failure is a case of its own
            var seen = _keys.GetValueOrDefault(key) + 1;
            _keys[key] = seen;
            if (seen > 1)
            {
                key = $"{key}#{seen}";
            }

            Cases.Add(new ModelTestCase(key, status, message, info?.Id, info?.Label, related.Select(item => item.Id).ToList(), false,
                access.Point(evidence?.Point), access.Box(evidence?.Box),
                value?.Text, value is null ? null : !value.Exists ? "missing" : string.IsNullOrWhiteSpace(value.Text) ? "empty" : "set"));
        }

        public void Error(Element? element, Exception exception, string? where = null)
        {
            var message = $"{(where is null ? "The test threw" : $"{where} threw")} {exception.GetType().Name}: {exception.Message}";
            Add("error", message, element is null ? null : new Evidence { Element = element });
        }

        private ElementInfo Describe(Element element)
        {
            if (!_described.TryGetValue(element, out var info))
            {
                info = access.Describe(element);
                _described[element] = info;
            }

            return info;
        }
    }

    private sealed class ModelTestContext(IModelAccess access, CaseRecorder recorder, CancellationToken cancellationToken) : IModelTestContext
    {
        public Document Document => access.Document ?? throw new InvalidOperationException("There is no Revit document outside Revit.");

        public IModelAssert Assert { get; } = new ModelAssert(recorder);

        public CancellationToken Cancellation => cancellationToken;

        public IEnumerable<Element> Elements(params BuiltInCategory[] categories)
        {
            return access.Collect(new ElementSelector(categories, null));
        }

        public IEnumerable<T> Elements<T>() where T : Element
        {
            return access.Collect(new ElementSelector([], typeof(T))).OfType<T>();
        }
    }

    private sealed class ModelAssert(CaseRecorder recorder) : IModelAssert
    {
        public void True(bool condition, string message, Evidence? evidence = null)
        {
            if (!condition)
            {
                recorder.Add("failed", message, evidence);
            }
        }

        public void NotEmpty(Element element, string parameter)
        {
            var value = recorder.Access.Read(element, ValueSource.Parameter, parameter);
            var failure = new ValueCondition.NotEmpty().Fails(parameter, value);
            if (failure is not null)
            {
                recorder.Add("failed", failure, new Evidence { Element = element }, value);
            }
        }

        public void Empty<TItem>(IEnumerable<TItem> items, string message)
        {
            foreach (var item in items)
            {
                recorder.Add("failed", item is Element ? message : $"{message}: {item}",
                    item is Element element ? new Evidence { Element = element } : new Evidence { Key = item?.ToString() ?? "null" });
            }
        }

        public void Fail(string message, Evidence? evidence = null)
        {
            recorder.Add("failed", message, evidence);
        }

        public void Inconclusive(string reason, Evidence? evidence = null)
        {
            recorder.Add("inconclusive", reason, evidence);
        }
    }
}
