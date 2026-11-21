using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
    /// <summary>
    /// A set of model tests: checks of the open Revit model written as code. AnalyseTool finds every class of an extension
    /// that implements this interface (public, with a parameterless constructor), calls <see cref="Define"/> once when the
    /// extension loads, and runs the tests it declared from the Model tests page or over MCP.
    /// </summary>
    /// <example>
    /// <code>
    /// public sealed class BepProjectX : IModelTestSuite
    /// {
    ///     public void Define(ISuite s)
    ///     {
    ///         s.Name("BEP Project X");
    ///         s.Elements(BuiltInCategory.OST_Doors).Parameter("Fire Rating").NotEmpty().FixValue("EI30");
    ///         s.Elements&lt;Room&gt;().Where(room => room.Area > 0).Parameter("Number").NotEmpty().Unique();
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IModelTestSuite
    {
        /// <summary>
        /// Declares the tests. It only describes them: there is no model here — the model is reached inside the lambdas,
        /// which run when the tests run. Keep it cheap, it runs every time the extension loads.
        /// </summary>
        void Define(ISuite suite);
    }

    /// <summary>How bad a failing test is: what the page sorts and colours by.</summary>
    public enum TestSeverity
    {
        /// <summary>Worth knowing, nothing to fix.</summary>
        Info,

        /// <summary>Should be fixed.</summary>
        Warning,

        /// <summary>Must be fixed — the default.</summary>
        Error
    }

    /// <summary>The builder <see cref="IModelTestSuite.Define"/> receives.</summary>
    public interface ISuite
    {
        /// <summary>The name shown on the page; the class name when absent.</summary>
        ISuite Name(string name);

        /// <summary>A label on every test of the suite, for filtering: <c>Trait("Discipline", "ARC")</c>.</summary>
        ISuite Trait(string name, string value);

        /// <summary>The element instances of the categories (not their types).</summary>
        IElementSet<Element> Elements(params BuiltInCategory[] categories);

        /// <summary>The elements of a class: <c>Elements&lt;Room&gt;()</c>, <c>Elements&lt;Wall&gt;()</c>, <c>Elements&lt;WallType&gt;()</c>.</summary>
        IElementSet<T> Elements<T>() where T : Element;

        /// <summary>A test of the whole model: the body asserts through <see cref="IModelTestContext.Assert"/>.</summary>
        /// <param name="name">The name shown on the page; also the test's id unless <see cref="ITest.Id"/> sets one.</param>
        /// <param name="body">Runs on the Revit thread when the test runs.</param>
        /// <param name="file">Filled by the compiler: where the test is written.</param>
        /// <param name="line">Filled by the compiler.</param>
        ITest Test(string name, Action<IModelTestContext> body, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
    }

    /// <summary>Elements a test runs over: every element becomes a case of its own, passed or failed.</summary>
    /// <typeparam name="T">The element class.</typeparam>
    public interface IElementSet<T> where T : Element
    {
        /// <summary>Keeps the elements the predicate accepts. Runs on the Revit thread when the test runs.</summary>
        IElementSet<T> Where(Func<T, bool> predicate);

        /// <summary>A rule on an instance parameter, looked up by its name as Revit shows it (names depend on the language of
        /// Revit: a built-in parameter is safer by its <see cref="BuiltInParameter"/>).</summary>
        IValueRule<T> Parameter(string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

        /// <summary>A rule on a built-in instance parameter: the same in every language of Revit, unlike a name.</summary>
        IValueRule<T> Parameter(BuiltInParameter parameter, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

        /// <summary>A rule on a parameter of the element's type.</summary>
        IValueRule<T> TypeParameter(string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

        /// <summary>A rule on a built-in parameter of the element's type.</summary>
        IValueRule<T> TypeParameter(BuiltInParameter parameter, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

        /// <summary>A rule on the name of the element's type.</summary>
        IValueRule<T> TypeName([CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

        /// <summary>A rule on any value of the element.</summary>
        /// <param name="label">What the value is, for the messages: <c>"Level name"</c>.</param>
        /// <param name="value">Reads the value; runs on the Revit thread.</param>
        /// <param name="file">Filled by the compiler.</param>
        /// <param name="line">Filled by the compiler.</param>
        IValueRule<T> Value(string label, Func<T, string?> value, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

        /// <summary>A test written as code, run for every element: it asserts through <see cref="IModelTestContext.Assert"/>.</summary>
        /// <param name="name">The name shown on the page.</param>
        /// <param name="body">Runs on the Revit thread for each element.</param>
        /// <param name="file">Filled by the compiler.</param>
        /// <param name="line">Filled by the compiler.</param>
        IElementTest<T> Check(string name, Action<T, IModelTestContext> body, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
    }

    /// <summary>What every test can say about itself. Call these last: they end the chain.</summary>
    public interface ITest
    {
        /// <summary>
        /// A stable id. Accepted failures (the baseline) are kept by id: without one the id follows the name, and renaming the
        /// test, or changing what a rule checks, starts its baseline afresh.
        /// </summary>
        ITest Id(string id);

        /// <summary>The name shown on the page.</summary>
        ITest Name(string name);

        /// <summary>How bad a failure is; <see cref="TestSeverity.Error"/> by default.</summary>
        ITest Severity(TestSeverity severity);

        /// <summary>A label for filtering.</summary>
        ITest Trait(string name, string value);

        /// <summary>A slow test (geometry, the whole model): runs only when asked for, not with every run.</summary>
        ITest Slow();

        /// <summary>Does not run; the page shows the reason.</summary>
        ITest Skip(string reason);
    }

    /// <summary>A test that runs per element and can repair a failing one.</summary>
    /// <typeparam name="T">The element class.</typeparam>
    public interface IElementTest<T> : ITest where T : Element
    {
        /// <summary>
        /// A repair the page and AI clients offer for a failing element. It runs in a transaction AnalyseTool opens (one for all
        /// the elements repaired at once, undone with Ctrl+Z).
        /// </summary>
        IElementTest<T> Fix(string label, Action<T> apply);

        /// <inheritdoc cref="Fix(string, Action{T})"/>
        IElementTest<T> Fix(string label, Action<T, IModelTestContext> apply);
    }

    /// <summary>
    /// A rule on one value of every element: the conditions are checked in order, the first that fails is the message. A
    /// missing parameter fails every condition.
    /// </summary>
    /// <typeparam name="T">The element class.</typeparam>
    public interface IValueRule<T> : IElementTest<T> where T : Element
    {
        /// <summary>The value exists and is not blank.</summary>
        IValueRule<T> NotEmpty();

        /// <summary>The value equals the text, ignoring case.</summary>
        IValueRule<T> Equal(string expected);

        /// <summary>The value matches the regular expression.</summary>
        IValueRule<T> Matches(string pattern);

        /// <summary>The value is one of these, ignoring case.</summary>
        IValueRule<T> OneOf(params string[] values);

        /// <summary>
        ///     The value is a number between the two, inclusive, in Revit's internal units: lengths in feet — write
        ///     <c>InRange(100.Mm(), 20000.Mm())</c> — areas in square feet, angles in radians. The same suite checks the same
        ///     in a project in millimetres and one in metres; failures show the value in the units of the project.
        /// </summary>
        IValueRule<T> InRange(double min, double max);

        /// <summary>No two elements of the set share the value (blank values are left to <see cref="NotEmpty"/>).</summary>
        IValueRule<T> Unique();

        /// <summary>A repair that writes this value into the parameter (as typed in Revit, units included).</summary>
        IValueRule<T> FixValue(string value);
    }

    /// <summary>What a test body receives while it runs: the model and the assertions.</summary>
    public interface IModelTestContext
    {
        /// <summary>The model under test. The body runs on the Revit thread inside a transaction group that is always rolled
        /// back: a test cannot change the model.</summary>
        Document Document { get; }

        /// <summary>The element instances of the categories.</summary>
        IEnumerable<Element> Elements(params BuiltInCategory[] categories);

        /// <summary>The elements of a class.</summary>
        IEnumerable<T> Elements<T>() where T : Element;

        /// <summary>The assertions of this run.</summary>
        IModelAssert Assert { get; }

        /// <summary>Signalled when the user stops the run.</summary>
        CancellationToken Cancellation { get; }
    }

    /// <summary>
    /// Soft assertions: a failed one is recorded and the body goes on, so one run shows everything that is wrong. In a
    /// <see cref="IElementSet{T}.Check"/> a failure belongs to the element under test unless the evidence names another.
    /// </summary>
    public interface IModelAssert
    {
        /// <summary>Fails with the message when the condition is false.</summary>
        void True(bool condition, string message, Evidence? evidence = null);

        /// <summary>Fails when the parameter of the element is missing or blank.</summary>
        void NotEmpty(Element element, string parameter);

        /// <summary>Fails once for every item: an element item is the failing element itself.</summary>
        void Empty<TItem>(IEnumerable<TItem> items, string message);

        /// <summary>Fails.</summary>
        void Fail(string message, Evidence? evidence = null);

        /// <summary>Neither passed nor failed: Revit could not tell (a geometry operation threw, data is missing).</summary>
        void Inconclusive(string reason, Evidence? evidence = null);
    }

    /// <summary>What a failure points at: the page selects, isolates and draws it.</summary>
    public sealed class Evidence
    {
        /// <summary>The failing element; in a per-element check, the element under test when absent.</summary>
        public Element? Element { get; set; }

        /// <summary>Other elements involved: the other side of a clash, the duplicates.</summary>
        public IReadOnlyList<Element> Related { get; set; } = Array.Empty<Element>();

        /// <summary>A point to show.</summary>
        public XYZ? Point { get; set; }

        /// <summary>A box to show: a clearance zone, an overlap.</summary>
        public Outline? Box { get; set; }

        /// <summary>
        /// Tells failures of the same element apart in the baseline, when one element can fail one test in several ways
        /// (two clashes with different elements are told apart by <see cref="Related"/> already).
        /// </summary>
        public string? Key { get; set; }
    }

    /// <summary>Lengths in the units people think in. The Revit API measures in feet.</summary>
    public static class Units
    {
        /// <summary>Millimetres to feet: <c>900.Mm()</c>.</summary>
        public static double Mm(this double value) => value / 304.8;

        /// <inheritdoc cref="Mm(double)"/>
        public static double Mm(this int value) => value / 304.8;

        /// <summary>Metres to feet: <c>0.9.M()</c>.</summary>
        public static double M(this double value) => value / 0.3048;

        /// <inheritdoc cref="M(double)"/>
        public static double M(this int value) => value / 0.3048;
    }
}
