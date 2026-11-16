using System.Text.Json;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Notifications;
using AnalyseTool.Hosting.WebView.Controls;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Infrastructure.Extensions;

/// <summary>What a click on an extension button does: runs its command and shows the result, or opens its page.</summary>
public sealed partial class ExtensionButtonRunner(ICommandExecutor executor, IUserNotifier notifier, IWindows windows, ILogger<ExtensionButtonRunner> logger)
{
    private const int MaxResultLength = 4000;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Handles a click on the WPF thread of Revit; never throws.</summary>
    public void Click(ExtensionDescriptor extension, ExtensionButton button)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(button.Command))
            {
                OpenPage(extension, button);
            }
            else
            {
                _ = RunAsync(button.Command!, button.Name);
            }
        }
        catch (Exception exception)
        {
            LogClickFailed(logger, exception, extension.Id);
            notifier.Error(button.Name, exception.Message);
        }
    }

    /// <summary>Opens the page of a button in a window, or in the dock pane when the button or the extension says so.</summary>
    public void OpenPage(ExtensionDescriptor extension, ExtensionButton? button)
    {
        if (!ExtensionPages.Exists(extension, button))
        {
            notifier.Error(button?.Name ?? extension.Id, $"The page of '{extension.Id}' is missing: check ui.entryHtml in plugin.json.");
            return;
        }

        var page = ExtensionPages.For(extension, button);
        if (button?.Dockable ?? extension.Manifest.Ui?.Dockable ?? false)
        {
            windows.ShowDockPane(page);
            return;
        }

        windows.OpenPage(page, options =>
        {
            options.Title = button?.Name ?? extension.Id;
            options.Width = 900;
            options.Height = 700;
        });
    }

    /// <summary>
    ///     Runs the command with no arguments and shows what it returned. Not awaited by the click: the command reaches Revit
    ///     through the dispatcher, which needs the click handler to return first.
    /// </summary>
    private async Task RunAsync(string command, string title)
    {
        var result = await executor.ExecuteAsync(new CommandRequest(command, default, CommandSources.Ribbon)).ConfigureAwait(false);
        // A failure was shown by the pipeline already (ribbon source)
        if (result.Error is null && result.Value is { } value && Describe(value) is { Length: > 0 } text)
        {
            notifier.Info(title, text.Length <= MaxResultLength ? text : text[..MaxResultLength] + "\n…");
        }
    }

    private static string Describe(object value)
    {
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? "",
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => "",
            JsonElement element => JsonSerializer.Serialize(element, Indented),
            string text => text,
            _ => JsonSerializer.Serialize(value, value.GetType(), Indented)
        };
    }

    [LoggerMessage(LogLevel.Error, "A click on a button of extension {Id} failed")]
    private static partial void LogClickFailed(ILogger logger, Exception exception, string id);
}
