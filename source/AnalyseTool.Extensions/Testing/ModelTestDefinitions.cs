using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions.Testing;

/// <summary>What a test runs over and how.</summary>
public enum ModelTestKind
{
    /// <summary>A declarative rule on one value of every element.</summary>
    Rule,

    /// <summary>Code run for every element.</summary>
    Check,

    /// <summary>Code run once for the whole model.</summary>
    Model
}

/// <summary>Where a rule reads its value.</summary>
public enum ValueSource
{
    /// <summary>An instance parameter.</summary>
    Parameter,

    /// <summary>A parameter of the element's type.</summary>
    TypeParameter,

    /// <summary>The name of the element's type.</summary>
    TypeName,

    /// <summary>A lambda of the author.</summary>
    Custom
}

/// <summary>
///     How categories and built-in parameters are named for people: as this Revit shows them (in its language), or — without
///     Revit, and whenever Revit cannot say — by their API names.
/// </summary>
/// <param name="Category">The name of a category.</param>
/// <param name="Parameter">The name of a built-in parameter.</param>
public sealed record RevitNames(Func<BuiltInCategory, string> Category, Func<BuiltInParameter, string> Parameter)
{
    /// <summary>The API names: <c>Walls</c> for <c>OST_Walls</c>, <c>ALL_MODEL_MARK</c>.</summary>
    public static readonly RevitNames Api = new(ElementSelector.CategoryName, parameter => parameter.ToString());

    /// <summary>A name from Revit, the API name when Revit throws.</summary>
    public static RevitNames Safe(Func<BuiltInCategory, string> category, Func<BuiltInParameter, string> parameter)
    {
        return new RevitNames(
            value => Try(() => category(value)) ?? Api.Category(value),
            value => Try(() => parameter(value)) ?? Api.Parameter(value));
    }

    private static string? Try(Func<string> name)
    {
        try
        {
            var text = name();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>Which elements a test runs over: categories, or a class.</summary>
/// <param name="Categories">The categories; empty when <paramref name="ElementClass" /> is set.</param>
/// <param name="ElementClass">The element class; <see langword="null" /> for categories.</param>
public sealed record ElementSelector(IReadOnlyList<BuiltInCategory> Categories, Type? ElementClass)
{
    /// <summary>Gets what the selector picks, for people: <c>Doors, Windows</c> or <c>Room</c>.</summary>
    public string Describe(RevitNames? names = null)
    {
        var naming = names ?? RevitNames.Api;
        return ElementClass is not null ? ElementClass.Name : string.Join(", ", Categories.Select(naming.Category));
    }

    /// <summary><c>OST_StructuralFraming</c> → <c>Structural Framing</c>, without Revit.</summary>
    public static string CategoryName(BuiltInCategory category)
    {
        var name = category.ToString();
        if (name.StartsWith("OST_", StringComparison.Ordinal))
        {
            name = name[4..];
        }

        var builder = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(name[index]);
        }

        return builder.ToString();
    }
}

/// <summary>A value read from an element.</summary>
/// <param name="Exists">The parameter (or value) exists on the element.</param>
/// <param name="Text">The value as Revit shows it; <see langword="null" /> when it has none.</param>
/// <param name="Number">The value as a number in the units Revit shows, when it is one.</param>
public sealed record ModelValue(bool Exists, string? Text, double? Number)
{
    /// <summary>A parameter the element does not have.</summary>
    public static readonly ModelValue Missing = new(false, null, null);

    /// <summary>A text value; its number when it reads as one.</summary>
    public static ModelValue Of(string? text)
    {
        return new ModelValue(true, text, double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null);
    }

    /// <summary>Gets a value indicating whether the value is missing or blank.</summary>
    public bool IsBlank => !Exists || string.IsNullOrWhiteSpace(Text);
}

/// <summary>One condition of a rule: says why a value fails, or <see langword="null" /> when it passes.</summary>
internal abstract class ValueCondition
{
    public abstract string Describe();

    public abstract string? Fails(string label, ModelValue value);

    protected static string? Missing(string label, ModelValue value)
    {
        return value.Exists ? null : $"«{label}» is missing";
    }

    internal sealed class NotEmpty : ValueCondition
    {
        public override string Describe() => "not empty";

        public override string? Fails(string label, ModelValue value)
        {
            return Missing(label, value) ?? (string.IsNullOrWhiteSpace(value.Text) ? $"«{label}» is empty" : null);
        }
    }

    internal sealed class Equal(string expected) : ValueCondition
    {
        public override string Describe() => $"= '{expected}'";

        public override string? Fails(string label, ModelValue value)
        {
            return Missing(label, value)
                   ?? (string.Equals(value.Text?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase) ? null : $"«{label}» is '{value.Text}', expected '{expected}'");
        }
    }

    internal sealed class Matches(string pattern) : ValueCondition
    {
        private readonly Regex _regex = new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        public override string Describe() => $"matches {pattern}";

        public override string? Fails(string label, ModelValue value)
        {
            return Missing(label, value) ?? (_regex.IsMatch(value.Text ?? "") ? null : $"«{label}» '{value.Text}' does not match {pattern}");
        }
    }

    internal sealed class OneOf(IReadOnlyList<string> values) : ValueCondition
    {
        public override string Describe() => $"one of {string.Join(", ", values)}";

        public override string? Fails(string label, ModelValue value)
        {
            return Missing(label, value)
                   ?? (values.Any(allowed => string.Equals(allowed.Trim(), value.Text?.Trim(), StringComparison.OrdinalIgnoreCase))
                       ? null
                       : $"«{label}» '{value.Text}' is not one of {string.Join(", ", values)}");
        }
    }

    internal sealed class InRange(double min, double max) : ValueCondition
    {
        public override string Describe() => $"in {Format(min)}…{Format(max)}";

        public override string? Fails(string label, ModelValue value)
        {
            if (Missing(label, value) is { } missing)
            {
                return missing;
            }

            if (value.Number is not { } number)
            {
                return $"«{label}» '{value.Text}' is not a number";
            }

            return number >= min && number <= max ? null : $"«{label}» is {Format(number)}, expected {Format(min)}…{Format(max)}";
        }

        private static string Format(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

/// <summary>The value and the conditions of a rule.</summary>
internal sealed class ValueRuleSpec
{
    public required ValueSource Source { get; init; }

    /// <summary>The parameter name, or what the value is (<see cref="ValueSource.Custom" />, <see cref="ValueSource.TypeName" />).</summary>
    public required string Label { get; init; }

    /// <summary>The built-in parameter, when the rule names one instead of a name.</summary>
    public BuiltInParameter? BuiltIn { get; init; }

    /// <summary>Reads the value of a <see cref="ValueSource.Custom" /> rule.</summary>
    public Func<Element, string?>? Read { get; init; }

    public List<ValueCondition> Conditions { get; } = [];

    public bool Unique { get; set; }

    public string Describe(RevitNames? names = null)
    {
        var checks = Checks();
        var label = Name(names);
        var what = Source switch
        {
            ValueSource.TypeParameter => $"type {label}",
            _ => label
        };
        return checks.Length == 0 ? what : $"{what} · {checks}";
    }

    /// <summary>The parameter or value as people know it: a built-in parameter by the name Revit shows.</summary>
    public string Name(RevitNames? names = null)
    {
        return BuiltIn is { } builtIn ? (names ?? RevitNames.Api).Parameter(builtIn) : Label;
    }

    /// <summary>The conditions: <c>not empty, unique</c>.</summary>
    public string Checks()
    {
        var parts = Conditions.Select(condition => condition.Describe()).ToList();
        if (Unique)
        {
            parts.Add("unique");
        }

        return string.Join(", ", parts);
    }
}

/// <summary>A declared test, with what it runs.</summary>
internal sealed class TestDefinition
{
    public required string SuiteId { get; init; }

    public required string ExtensionId { get; init; }

    public required ModelTestKind Kind { get; init; }

    public required string File { get; init; }

    public required int Line { get; init; }

    /// <summary>The id within the suite; <see cref="Id" /> is the full one.</summary>
    public string? LocalId { get; set; }

    public string? ExplicitName { get; set; }

    public string Id => $"{SuiteId}/{LocalId}";

    /// <summary>
    ///     The id the baseline files the accepted failures under: <c>&lt;suite class&gt;/&lt;test id&gt;</c>. Suites of one
    ///     extension share the baseline file, so the suite must be in the key; the extension (= the folder) must not, or
    ///     renaming the folder would orphan every baseline in it.
    /// </summary>
    public string BaselineId => $"{(SuiteId.StartsWith(ExtensionId + ".", StringComparison.OrdinalIgnoreCase) ? SuiteId[(ExtensionId.Length + 1)..] : SuiteId)}/{LocalId}";

    public string Name => ExplicitName ?? DefaultName;

    /// <summary>The name of a code test, or the rule described.</summary>
    public required string DefaultName { get; set; }

    public TestSeverity Severity { get; set; } = TestSeverity.Error;

    public List<KeyValuePair<string, string>> Traits { get; } = [];

    public bool Slow { get; set; }

    public string? SkipReason { get; set; }

    public ElementSelector? Selector { get; init; }

    public List<Func<Element, bool>> Filters { get; init; } = [];

    public ValueRuleSpec? Rule { get; init; }

    public Action<Element, IModelTestContext>? ElementBody { get; init; }

    public Action<IModelTestContext>? ModelBody { get; init; }

    public string? FixLabel { get; set; }

    public Action<Element, IModelTestContext>? Fix { get; set; }

    /// <summary>The value a <see cref="IValueRule{T}.FixValue" /> repair writes.</summary>
    public string? FixValue { get; set; }

    /// <summary>The name shown: the author's, or the rule described in the names of this Revit.</summary>
    public string DisplayName(RevitNames? names = null)
    {
        return ExplicitName ?? (Kind == ModelTestKind.Rule ? Describe(names) : DefaultName);
    }

    public string Describe(RevitNames? names = null)
    {
        var where = Filters.Count > 0 ? " (filtered)" : "";
        return Kind switch
        {
            ModelTestKind.Rule => $"{Selector!.Describe(names)}{where} · {Rule!.Describe(names)}",
            ModelTestKind.Check => $"{Selector!.Describe(names)}{where} · code",
            _ => "Whole model · code"
        };
    }

    public bool HasFix => Fix is not null || FixValue is not null;
}

/// <summary>A suite as declared by its class.</summary>
internal sealed class SuiteDefinition
{
    public required string Id { get; init; }

    public required string ExtensionId { get; init; }

    public required string ClassName { get; init; }

    public string? Name { get; set; }

    public string DisplayName => Name ?? ClassName;

    /// <summary>The extension folder.</summary>
    public required string Directory { get; init; }

    /// <summary>The name of the extension.</summary>
    public string? ExtensionName { get; init; }

    /// <summary>Where the accepted failures of this suite live: the extension folder, or the profile for an installed package.</summary>
    public required string BaselineDirectory { get; init; }

    public List<KeyValuePair<string, string>> Traits { get; } = [];

    public List<TestDefinition> Tests { get; } = [];
}
