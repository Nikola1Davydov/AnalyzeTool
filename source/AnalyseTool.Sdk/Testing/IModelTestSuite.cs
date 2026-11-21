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
}
