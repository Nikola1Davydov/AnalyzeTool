using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
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
}
