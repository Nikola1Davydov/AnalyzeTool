using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
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
}
