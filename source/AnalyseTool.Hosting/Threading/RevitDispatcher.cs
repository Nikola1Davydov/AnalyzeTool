using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Hosting.Threading;

/// <summary>
///     <see cref="IRevitDispatcher" /> over one long-lived <c>AsyncRequestExternalEvent</c> from Nice3point.Revit.Toolkit.
/// </summary>
/// <remarks>
///     <para>
///         Must be created in a valid Revit API context (<c>ExternalEvent.Create</c> requires it), so the module creates it while
///         the add-in starts. <see cref="ExternalEventOptions.AllowDirectInvocation" /> runs the work directly when the caller is
///         already in API mode — for example a button's <c>Execute</c> — instead of queueing an event that could not run until
///         the caller returns.
///     </para>
///     <para>
///         Each call is one external event, and Revit rolls back what is still open when it returns. A transaction the work
///         leaves open is reported right there, with what to do, instead of failing later with a riddle (a TransactionGroup
///         that is gone by the time it is assimilated). An open TransactionGroup alone cannot be seen through the API.
///     </para>
/// </remarks>
internal sealed class RevitDispatcher : IRevitDispatcher
{
    private readonly AsyncRequestExternalEvent<Func<UIApplication, object?>, object?> _event =
        new(static (application, work, _) => Run(application, work), ExternalEventOptions.AllowDirectInvocation);

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

    private static object? Run(UIApplication application, Func<UIApplication, object?> work)
    {
        // A direct invocation may run inside the caller's own transaction: only one the work opened counts
        var openBefore = OpenTransactions(application);
        var result = work(application);
        var leftOpen = OpenTransactions(application).Except(openBefore).ToList();
        if (leftOpen.Count > 0)
        {
            throw new InvalidOperationException(
                $"A transaction is still open in {string.Join(", ", leftOpen.Select(title => $"«{title}»"))} at the end of RunInRevitAsync; Revit rolls it back. " +
                "Start and end every Transaction and TransactionGroup inside ONE RunInRevitAsync — a call is one external event of Revit, " +
                "and nothing stays open between two calls.");
        }

        return result;
    }

    private static HashSet<string> OpenTransactions(UIApplication application)
    {
        var titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (Document document in application.Application.Documents)
        {
            if (document.IsValidObject && document.IsModifiable)
            {
                titles.Add(document.Title);
            }
        }

        return titles;
    }
}
