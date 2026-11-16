using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Threading;

namespace AnalyseTool.Services.Documents;

/// <summary>
///     The document a pipeline command works with: the active one, fixed for the whole call.
/// </summary>
/// <remarks>
///     <para>
///         Scoped, so each call gets its own instance. The first access remembers the active document; when the user switches
///         documents before a later access, that access fails with <see cref="DocumentErrorCodes.DocumentChanged" /> instead of
///         touching another model. <see cref="DocumentCheckMiddleware" /> can fix the document up front from the caller's
///         header, so data the caller loaded from one document is never applied to another.
///     </para>
///     <para>
///         This is the template's choice, not the module's: an add-in that needs another rule replaces this class and the
///         middleware.
///     </para>
/// </remarks>
[PublicAPI]
public sealed class ActiveDocument(IRevitDispatcher dispatcher)
{
    private string? _key;

    /// <summary>Gets the key of the document of this call, once it is known.</summary>
    public string? Key => _key;

    /// <summary>
    ///     Returns the key that tells documents apart: the file path, or the title for an unsaved document. «Save As» changes
    ///     it, and the caller has to reload its data — as after a switch.
    /// </summary>
    public static string GetKey(Document document)
    {
        return string.IsNullOrEmpty(document.PathName) ? document.Title : document.PathName;
    }

    /// <summary>Runs <paramref name="work" /> on the Revit thread with the document of this call.</summary>
    /// <exception cref="CommandException">No document is open, or the active document is not the one of this call.</exception>
    public Task<T> InvokeAsync<T>(Func<Document, T> work, CancellationToken cancellationToken = default)
    {
        return dispatcher.InvokeAsync(application => work(Resolve(application).Document), cancellationToken);
    }

    /// <inheritdoc cref="InvokeAsync{T}(Func{Document,T},CancellationToken)" />
    public Task InvokeAsync(Action<Document> work, CancellationToken cancellationToken = default)
    {
        return dispatcher.InvokeAsync(application => work(Resolve(application).Document), cancellationToken);
    }

    /// <summary>Runs <paramref name="work" /> on the Revit thread with the <c>UIDocument</c> of this call: selection, views.</summary>
    /// <inheritdoc cref="InvokeAsync{T}(Func{Document,T},CancellationToken)" path="/exception" />
    public Task<T> InvokeUIAsync<T>(Func<UIDocument, T> work, CancellationToken cancellationToken = default)
    {
        return dispatcher.InvokeAsync(application => work(Resolve(application)), cancellationToken);
    }

    /// <summary>
    ///     Runs <paramref name="work" /> over <paramref name="items" /> in batches, one Revit call per batch: between batches
    ///     Revit gets control back, so the page shows progress and the user can cancel.
    /// </summary>
    /// <remarks>
    ///     Every batch checks the document like any access. Writing in batches means one transaction — one undo step — per
    ///     batch; reading in batches costs nothing.
    /// </remarks>
    /// <inheritdoc cref="InvokeAsync{T}(Func{Document,T},CancellationToken)" path="/exception" />
    public async Task InvokeBatchedAsync<TItem>(
        IReadOnlyList<TItem> items,
        int batchSize,
        Action<Document, IReadOnlyList<TItem>> work,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        for (var start = 0; start < items.Count; start += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = items.Skip(start).Take(batchSize).ToList();
            await InvokeAsync(document => work(document, batch), cancellationToken).ConfigureAwait(false);

            var done = Math.Min(start + batchSize, items.Count);
            progress?.Report(new ProgressInfo((double)done / items.Count, $"{done} / {items.Count}"));
        }
    }

    /// <summary>Fixes the document of this call before the first access; used by <see cref="DocumentCheckMiddleware" />.</summary>
    internal void Expect(string key)
    {
        _key = key;
    }

    private UIDocument Resolve(UIApplication application)
    {
        var document = application.ActiveUIDocument
                       ?? throw new CommandException(DocumentErrorCodes.NoDocument, "Open a document first.");

        var key = GetKey(document.Document);
        if (_key is not null && _key != key)
        {
            throw new CommandException(DocumentErrorCodes.DocumentChanged, "The active document has changed. Refresh the data and try again.");
        }

        _key = key;
        return document;
    }
}

/// <summary>
///     The error codes of <see cref="ActiveDocument" />, next to the module's <see cref="CommandErrorCodes" />.
/// </summary>
[PublicAPI]
public static class DocumentErrorCodes
{
    /// <summary>No document is open.</summary>
    public const string NoDocument = "no_document";

    /// <summary>The active document is not the one the call started with or the caller expects.</summary>
    public const string DocumentChanged = "document_changed";
}
