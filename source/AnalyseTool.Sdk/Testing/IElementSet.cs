using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
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
}
