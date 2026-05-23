using Autodesk.Revit.UI;

namespace AnalyseTool.Sdk
{
    public interface IRevitContext
    {
        RevitPayload Payload { get; }

        /// <summary>
        /// Runs <paramref name="work"/> inside a valid Revit API context (transactions allowed)
        /// and returns its result. This is the ONLY place the Revit model may be touched: reads and
        /// writes both go here. Keep long-running I/O (HTTP, AI) OUTSIDE this call — the body runs
        /// synchronously on the Revit thread.
        /// </summary>
        Task<T> RunInRevitAsync<T>(Func<UIApplication, T> work);

        /// <summary>
        /// Runs <paramref name="work"/> inside a valid Revit API context (transactions allowed).
        /// </summary>
        Task RunInRevitAsync(Action<UIApplication> work);
    }
}
