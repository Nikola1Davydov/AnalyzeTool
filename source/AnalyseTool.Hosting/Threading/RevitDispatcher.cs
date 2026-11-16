using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Hosting.Threading;

/// <summary>
///     <see cref="IRevitDispatcher" /> over one long-lived <c>AsyncRequestExternalEvent</c> from Nice3point.Revit.Toolkit.
/// </summary>
/// <remarks>
///     Must be created in a valid Revit API context (<c>ExternalEvent.Create</c> requires it), so the module creates it while
///     the add-in starts. <see cref="ExternalEventOptions.AllowDirectInvocation" /> runs the work directly when the caller is
///     already in API mode — for example a button's <c>Execute</c> — instead of queueing an event that could not run until
///     the caller returns.
/// </remarks>
internal sealed class RevitDispatcher : IRevitDispatcher
{
    private readonly AsyncRequestExternalEvent<Func<UIApplication, object?>, object?> _event =
        new(static (application, work, _) => work(application), ExternalEventOptions.AllowDirectInvocation);

    /// <inheritdoc />
    public async Task<T> InvokeAsync<T>(Func<UIApplication, T> work, CancellationToken cancellationToken = default)
    {
        var result = await _event.RaiseAsync(application => work(application), cancellationToken).ConfigureAwait(false);
        return (T)result!;
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action<UIApplication> work, CancellationToken cancellationToken = default)
    {
        return _event.RaiseAsync(application =>
        {
            work(application);
            return null;
        }, cancellationToken);
    }
}
