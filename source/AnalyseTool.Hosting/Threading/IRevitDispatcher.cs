using Autodesk.Revit.UI;

namespace AnalyseTool.Hosting.Threading;

/// <summary>
///     The only way to reach the Revit API from code outside a Revit API context.
/// </summary>
/// <remarks>
///     <para>
///         The work runs synchronously on the Revit thread, which is also the UI thread: keep it short and do slow I/O outside.
///         Callable from any thread; calls run one after another in call order.
///     </para>
///     <para>
///         Which document to work with is the add-in's decision: it wraps the dispatcher in its own service, as
///         <c>ActiveDocument</c> in the template.
///     </para>
/// </remarks>
[PublicAPI]
public interface IRevitDispatcher
{
    /// <summary>
    ///     Runs <paramref name="work" /> inside a valid Revit API context and returns its result.
    /// </summary>
    /// <param name="work">The work; transactions are allowed.</param>
    /// <param name="cancellationToken">Cancels the call while it waits in the queue; a running delegate is not interrupted.</param>
    Task<T> InvokeAsync<T>(Func<UIApplication, T> work, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs <paramref name="work" /> inside a valid Revit API context.
    /// </summary>
    /// <param name="work">The work; transactions are allowed.</param>
    /// <param name="cancellationToken">Cancels the call while it waits in the queue; a running delegate is not interrupted.</param>
    Task InvokeAsync(Action<UIApplication> work, CancellationToken cancellationToken = default);
}
