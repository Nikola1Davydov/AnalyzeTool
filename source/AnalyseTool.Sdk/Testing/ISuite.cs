using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
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
}
