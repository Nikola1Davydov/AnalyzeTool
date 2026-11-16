using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Threading;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Notifications;

/// <summary>
///     Tells the user about something when there is no window to answer to — for example, a command started from the ribbon.
/// </summary>
/// <remarks>Callable from any thread; the dialog is shown on the Revit thread. Does not wait for the user to close it.</remarks>
[PublicAPI]
public interface IUserNotifier
{
    /// <summary>Shows an error.</summary>
    void Error(string title, string message);

    /// <summary>Shows an information message.</summary>
    void Info(string title, string message);
}

/// <summary>
///     <see cref="IUserNotifier" /> with Revit task dialogs. The WebView module may replace it with in-page notifications later.
/// </summary>
internal sealed partial class TaskDialogNotifier(IRevitDispatcher dispatcher, ILogger<TaskDialogNotifier> logger) : IUserNotifier
{
    /// <inheritdoc />
    public void Error(string title, string message)
    {
        _ = ShowAsync(title, message, TaskDialogIcon.TaskDialogIconError);
    }

    /// <inheritdoc />
    public void Info(string title, string message)
    {
        _ = ShowAsync(title, message, TaskDialogIcon.TaskDialogIconInformation);
    }

    private async Task ShowAsync(string title, string message, TaskDialogIcon icon)
    {
        try
        {
            await dispatcher.InvokeAsync(_ =>
            {
                var dialog = new TaskDialog(title)
                {
                    MainInstruction = title,
                    MainContent = message,
                    MainIcon = icon
                };

                dialog.Show();
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogShowFailed(logger, exception, title, message);
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not show the dialog '{Title}': {Message}")]
    private static partial void LogShowFailed(ILogger logger, Exception exception, string title, string message);
}
