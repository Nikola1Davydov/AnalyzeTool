using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
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
}
