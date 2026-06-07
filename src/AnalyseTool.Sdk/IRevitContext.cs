using Autodesk.Revit.UI;

namespace AnalyseTool.Sdk
{
    /// <summary>
    /// What a command receives: the request <see cref="Payload"/> and the only sanctioned way to touch
    /// the Revit model (<see cref="RunInRevitAsync{T}"/>). Intentionally minimal — no direct
    /// Document/UIApplication accessors, so model access can't happen off the Revit thread.
    /// </summary>
    public interface IRevitContext
    {
        /// <summary>The JSON payload the caller passed to <c>AT.invoke(command, payload)</c> (or the MCP
        /// tool arguments). Deserialize it with <c>Payload.As&lt;T&gt;()</c>.</summary>
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
