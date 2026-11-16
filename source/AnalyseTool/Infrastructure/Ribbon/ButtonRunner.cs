using System.Diagnostics;
using System.Text.Json;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Extensions.Usage;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Notifications;
using AnalyseTool.Hosting.WebView.Controls;
using AnalyseTool.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     What a click on any ribbon button does — AnalyseTool's own or an extension's: count it, then run its command through the
///     pipeline (with the button's <c>args</c>) and show what it returned, or open its page.
/// </summary>
public sealed partial class ButtonRunner(ICommandExecutor executor, ButtonUsageStore usage, IUserNotifier notifier, IWindows windows, ILogger<ButtonRunner> logger)
{
    private const int MaxResultLength = 4000;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Handles a click on the WPF thread of Revit; never throws.</summary>
    public void Click(ExtensionDescriptor extension, ExtensionButton button)
    {
        try
        {
            var command = string.IsNullOrWhiteSpace(button.Command) ? null : button.Command;
            usage.RecordClick(extension.Id, button.Name, command);
            if (command is null)
            {
                OpenPage(extension, button);
            }
            else
            {
                _ = RunAsync(extension.Id, button, command);
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
            options.Title = ButtonUsageStore.Normalize(button?.Name ?? extension.Id);
            options.Width = 900;
            options.Height = 700;
        });
    }

    /// <summary>
    ///     Runs the command and shows what it returned. Not awaited by the click: the command reaches Revit through the
    ///     dispatcher, which needs the click handler to return first.
    /// </summary>
    private async Task RunAsync(string extension, ExtensionButton button, string command)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(new CommandRequest(command, button.Args ?? default, CommandSources.Ribbon)).ConfigureAwait(false);
        usage.RecordResult(extension, button.Name, result.Error is null, stopwatch.Elapsed, result.Error?.Message);

        // A failure was shown by the pipeline already (ribbon source)
        if (result.Error is null && result.Value is { } value && Describe(value) is { Length: > 0 } text)
        {
            notifier.Info(ButtonUsageStore.Normalize(button.Name), text.Length <= MaxResultLength ? text : text[..MaxResultLength] + "\n…");
        }
    }

    /// <summary>The text a result is shown as; empty for nothing worth a dialog (<see cref="Empty" />, <c>null</c>, <c>{}</c>).</summary>
    internal static string Describe(object value)
    {
        return value switch
        {
            Empty => "",
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? "",
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => "",
            JsonElement { ValueKind: JsonValueKind.Object } element when !element.EnumerateObject().Any() => "",
            JsonElement element => JsonSerializer.Serialize(element, Indented),
            string text => text,
            _ => JsonSerializer.Serialize(value, value.GetType(), Indented) is var json && json != "{}" ? json : ""
        };
    }

    [LoggerMessage(LogLevel.Error, "A click on a button of {Id} failed")]
    private static partial void LogClickFailed(ILogger logger, Exception exception, string id);
}
