using System.IO;
using System.Text.RegularExpressions;
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions.Testing;

/// <summary>
///     The fluent builder of the Sdk: records what <see cref="IModelTestSuite.Define" /> declares into a
///     <see cref="SuiteDefinition" />. Nothing here touches Revit — the lambdas are kept and run later by the engine.
/// </summary>
internal sealed partial class SuiteBuilder(SuiteDefinition suite, string extensionDirectory) : ISuite
{
    public ISuite Name(string name)
    {
        suite.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return this;
    }

    public ISuite Trait(string name, string value)
    {
        suite.Traits.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    public IElementSet<Element> Elements(params BuiltInCategory[] categories)
    {
        if (categories.Length == 0)
        {
            throw new ArgumentException("Name at least one category.", nameof(categories));
        }

        return new ElementSet<Element>(this, new ElementSelector(categories.Distinct().ToList(), null), []);
    }

    public IElementSet<T> Elements<T>() where T : Element
    {
        return new ElementSet<T>(this, new ElementSelector([], typeof(T)), []);
    }

    public ITest Test(string name, Action<IModelTestContext> body, string file = "", int line = 0)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new TestHandle(Add(new TestDefinition
        {
            SuiteId = suite.Id,
            ExtensionId = suite.ExtensionId,
            Kind = ModelTestKind.Model,
            DefaultName = RequireName(name),
            ModelBody = body,
            File = Relative(file),
            Line = line
        }));
    }

    /// <summary>Gives every test an id: its own, or one made from its name; a repeated id gets a number.</summary>
    /// <returns>The problems: ids given twice on purpose.</returns>
    public IReadOnlyList<string> Complete()
    {
        var problems = new List<string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var test in suite.Tests.Where(test => test.LocalId is not null))
        {
            if (taken.Add(test.LocalId!))
            {
                continue;
            }

            // Two tests with one id would share a baseline and a repair: the second one gets a number, and the author is told
            var unique = test.LocalId!;
            for (var number = 2; !taken.Add(unique); number++)
            {
                unique = $"{test.LocalId}-{number}";
            }

            problems.Add($"{suite.Id}: the id '{test.LocalId}' is given to two tests; the one at {test.File}:{test.Line} runs as '{unique}'.");
            test.LocalId = unique;
        }

        foreach (var test in suite.Tests.Where(test => test.LocalId is null))
        {
            var id = Slug(test.Name);
            var unique = id;
            for (var number = 2; !taken.Add(unique); number++)
            {
                unique = $"{id}-{number}";
            }

            test.LocalId = unique;
        }

        return problems;
    }

    internal static string Slug(string text)
    {
        var slug = NonSlug().Replace(text.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "test" : slug;
    }

    private TestDefinition Add(TestDefinition test)
    {
        foreach (var trait in suite.Traits)
        {
            test.Traits.Add(trait);
        }

        suite.Tests.Add(test);
        return test;
    }

    private static string RequireName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A test needs a name.", nameof(name)) : name.Trim();
    }

    /// <summary>The file a test is written in, relative to the extension folder when it is in it.</summary>
    private string Relative(string file)
    {
        if (string.IsNullOrEmpty(file) || !Path.IsPathRooted(file))
        {
            return file;
        }

        var relative = Path.GetRelativePath(extensionDirectory, file);
        return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(file) : relative;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    private sealed class ElementSet<T>(SuiteBuilder owner, ElementSelector selector, IReadOnlyList<Func<T, bool>> predicates) : IElementSet<T> where T : Element
    {
        public IElementSet<T> Where(Func<T, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return new ElementSet<T>(owner, selector, [.. predicates, predicate]);
        }

        public IValueRule<T> Parameter(string name, string file = "", int line = 0)
        {
            return Rule(ValueSource.Parameter, RequireName(name), null, file, line);
        }

        public IValueRule<T> Parameter(BuiltInParameter parameter, string file = "", int line = 0)
        {
            return Rule(ValueSource.Parameter, parameter.ToString(), null, file, line, parameter);
        }

        public IValueRule<T> TypeParameter(string name, string file = "", int line = 0)
        {
            return Rule(ValueSource.TypeParameter, RequireName(name), null, file, line);
        }

        public IValueRule<T> TypeParameter(BuiltInParameter parameter, string file = "", int line = 0)
        {
            return Rule(ValueSource.TypeParameter, parameter.ToString(), null, file, line, parameter);
        }

        public IValueRule<T> TypeName(string file = "", int line = 0)
        {
            return Rule(ValueSource.TypeName, "Type name", null, file, line);
        }

        public IValueRule<T> Value(string label, Func<T, string?> value, string file = "", int line = 0)
        {
            ArgumentNullException.ThrowIfNull(value);
            return Rule(ValueSource.Custom, RequireName(label), element => value((T)element), file, line);
        }

        public IElementTest<T> Check(string name, Action<T, IModelTestContext> body, string file = "", int line = 0)
        {
            ArgumentNullException.ThrowIfNull(body);
            var test = owner.Add(new TestDefinition
            {
                SuiteId = owner.Id,
                ExtensionId = owner.ExtensionId,
                Kind = ModelTestKind.Check,
                DefaultName = RequireName(name),
                Selector = selector,
                Filters = Filters(),
                ElementBody = (element, context) => body((T)element, context),
                File = owner.Relative(file),
                Line = line
            });
            return new ValueRule<T>(test);
        }

        private ValueRule<T> Rule(ValueSource source, string label, Func<Element, string?>? read, string file, int line, BuiltInParameter? builtIn = null)
        {
            var spec = new ValueRuleSpec { Source = source, Label = label, Read = read, BuiltIn = builtIn };
            var test = owner.Add(new TestDefinition
            {
                SuiteId = owner.Id,
                ExtensionId = owner.ExtensionId,
                Kind = ModelTestKind.Rule,
                DefaultName = label,
                Selector = selector,
                Filters = Filters(),
                Rule = spec,
                File = owner.Relative(file),
                Line = line
            });
            test.DefaultName = test.Describe();
            return new ValueRule<T>(test);
        }

        private List<Func<Element, bool>> Filters()
        {
            // The element class is checked by the collector already; the cast is the author's type
            return predicates.Select(predicate => (Func<Element, bool>)(element => element is T typed && predicate(typed))).ToList();
        }
    }

    private string Id => suite.Id;

    private string ExtensionId => suite.ExtensionId;

    private class TestHandle(TestDefinition test) : ITest
    {
        protected TestDefinition Test { get; } = test;

        public ITest Id(string id)
        {
            Test.LocalId = string.IsNullOrWhiteSpace(id) ? null : Slug(id);
            return this;
        }

        public ITest Name(string name)
        {
            Test.ExplicitName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            return this;
        }

        public ITest Severity(TestSeverity severity)
        {
            Test.Severity = severity;
            return this;
        }

        public ITest Trait(string name, string value)
        {
            Test.Traits.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }

        public ITest Slow()
        {
            Test.Slow = true;
            return this;
        }

        public ITest Skip(string reason)
        {
            Test.SkipReason = string.IsNullOrWhiteSpace(reason) ? "Skipped" : reason.Trim();
            return this;
        }
    }

    private sealed class ValueRule<T>(TestDefinition test) : TestHandle(test), IValueRule<T> where T : Element
    {
        public IElementTest<T> Fix(string label, Action<T> apply)
        {
            ArgumentNullException.ThrowIfNull(apply);
            return Fix(label, (element, _) => apply(element));
        }

        public IElementTest<T> Fix(string label, Action<T, IModelTestContext> apply)
        {
            ArgumentNullException.ThrowIfNull(apply);
            Test.FixLabel = RequireName(label);
            Test.Fix = (element, context) => apply((T)element, context);
            Test.FixValue = null;
            return this;
        }

        public IValueRule<T> NotEmpty() => With(new ValueCondition.NotEmpty());

        public IValueRule<T> Equal(string expected) => With(new ValueCondition.Equal(expected ?? ""));

        public IValueRule<T> Matches(string pattern) => With(new ValueCondition.Matches(pattern));

        public IValueRule<T> OneOf(params string[] values) => With(new ValueCondition.OneOf(values));

        public IValueRule<T> InRange(double min, double max) => With(new ValueCondition.InRange(Math.Min(min, max), Math.Max(min, max)));

        public IValueRule<T> Unique()
        {
            RequireRule().Unique = true;
            return Refresh();
        }

        public IValueRule<T> FixValue(string value)
        {
            if (RequireRule().Source is not (ValueSource.Parameter or ValueSource.TypeParameter))
            {
                throw new InvalidOperationException("FixValue writes a parameter: use it on Parameter(...) or TypeParameter(...), or write the repair with Fix(...).");
            }

            Test.FixValue = value ?? "";
            Test.FixLabel = $"Set to '{value}'";
            Test.Fix = null;
            return this;
        }

        private ValueRule<T> With(ValueCondition condition)
        {
            RequireRule().Conditions.Add(condition);
            return Refresh();
        }

        private ValueRule<T> Refresh()
        {
            Test.DefaultName = Test.Describe();
            return this;
        }

        private ValueRuleSpec RequireRule()
        {
            return Test.Rule ?? throw new InvalidOperationException("A Check has no value conditions: assert in its body instead.");
        }
    }
}
