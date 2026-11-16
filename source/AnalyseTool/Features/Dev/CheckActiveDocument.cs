#if DEBUG
using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Notifications;

namespace AnalyseTool.Features.Dev;

/// <summary>
///     Debug-only pipeline command, sent by the «Active document» button: reads the active document twice, a few seconds
///     apart. Switch documents in between to see the change check of <see cref="ActiveDocument" /> (docs/manual-tests.md).
/// </summary>
[Command(Description = "Reads the active document twice, 5 seconds apart, and shows it.")]
public sealed class CheckActiveDocument(ActiveDocument document, IUserNotifier notifier) : ICommand<ActiveDocumentInfo>
{
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<ActiveDocumentInfo> ExecuteAsync(CommandContext context)
    {
        var title = await document.InvokeAsync(revitDocument => revitDocument.Title, context.CancellationToken);
        await Task.Delay(Pause, context.CancellationToken);

        var elementCount = await document.InvokeAsync(
            revitDocument => new FilteredElementCollector(revitDocument).WhereElementIsNotElementType().GetElementCount(),
            context.CancellationToken);

        var info = new ActiveDocumentInfo(document.Key!, title, elementCount);
        notifier.Info("AnalyseTool", $"Active document: {info.Title}\nKey: {info.Key}\nElements: {info.ElementCount}");
        return info;
    }
}

/// <summary>The result of <see cref="CheckActiveDocument" />.</summary>
/// <param name="Key">The document key from <see cref="ActiveDocument.GetKey" />.</param>
/// <param name="Title">The document title.</param>
/// <param name="ElementCount">The number of model elements, without types.</param>
public sealed record ActiveDocumentInfo(string Key, string Title, int ElementCount);
#endif
