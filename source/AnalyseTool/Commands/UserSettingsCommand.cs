#if DEBUG
using Autodesk.Revit.Attributes;
using AnalyseTool.Infrastructure.Diagnostics;
using AnalyseTool.Hosting.Notifications;
using AnalyseTool.Hosting.Settings;
using Microsoft.Extensions.Options;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Commands;

/// <summary>
///     Debug-only button that writes a user setting and shows the value <c>IOptionsMonitor</c> currently sees.
///     Click a few times, restart Revit, click again: the counter continues.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class UserSettingsCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        var settings = Host.GetService<IUserSettings>();
        var options = Host.GetService<IOptionsMonitor<DevOptions>>();
        var notifier = Host.GetService<IUserNotifier>();

        // A button must not block on async work: the write finishes in the background, the dialog comes after it
        _ = Task.Run(async () =>
        {
            await settings.UpdateAsync<DevOptions>(DevOptions.SectionName, dev => dev.ClickCount++);
            await Task.Delay(TimeSpan.FromMilliseconds(500));   // the file watcher reloads the configuration

            notifier.Info("AnalyseTool", $"Clicks: {options.CurrentValue.ClickCount}\nFile: {settings.FilePath}");
        });
    }
}
#endif
