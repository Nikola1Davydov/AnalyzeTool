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

/// <summary>How a click ended.</summary>
/// <param name="Ok">The command succeeded, or the page opened.</param>
/// <param name="Text">What the command returned, as text; <see langword="null" /> when there is nothing to show.</param>
/// <param name="Error">Why it failed.</param>
/// <param name="ElapsedMilliseconds">How long the command took; 0 for a page.</param>
/// <param name="OpenedPage">The button opened a page instead of running a command.</param>
public sealed record ButtonRunResult(bool Ok, string? Text, string? Error, double ElapsedMilliseconds, bool OpenedPage);

/// <summary>
///     What a click on any ribbon button does — AnalyseTool's own or an extension's: count it, then run its command through the
///     pipeline (with the button's <c>args</c>) and show what it returned, or open its page.
/// </summary>
public sealed partial class ButtonRunner(ICommandExecutor executor, ButtonUsageStore usage, IUserNotifier notifier, IWindows windows, ILogger<ButtonRunner> logger)
{
    private const int MaxResultLength = 4000;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Handles a click on the ribbon, on the WPF thread of Revit; never throws. The result is shown in a dialog.</summary>
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
                _ = RunAndShowAsync(extension.Id, button, command);
            }
        }
        catch (Exception exception)
        {
            LogClickFailed(logger, exception, extension.Id);
            notifier.Error(button.Name, exception.Message);
        }
    }

    /// <summary>
    ///     A click from the dock pane: counted like a ribbon click, but the result goes back to the caller (the pane shows it)
    ///     instead of a dialog. Opening a page needs the WPF thread: <paramref name="onUiThread" /> runs it there.
    /// </summary>
    public async Task<ButtonRunResult> RunAsync(ExtensionDescriptor extension, ExtensionButton button, Func<Action, Task> onUiThread)
    {
        var command = string.IsNullOrWhiteSpace(button.Command) ? null : button.Command;
        usage.RecordClick(extension.Id, button.Name, command);
        if (command is null)
        {
            await onUiThread(() => OpenPage(extension, button)).ConfigureAwait(false);
            return new ButtonRunResult(true, null, null, 0, true);
        }

        var (result, elapsed) = await ExecuteAsync(extension.Id, button, command, CommandSources.WebView).ConfigureAwait(false);
        return result.Error is null
            ? new ButtonRunResult(true, result.Value is { } value && Describe(value) is { Length: > 0 } text ? text : null, null, elapsed.TotalMilliseconds, false)
            : new ButtonRunResult(false, null, result.Error.Message, elapsed.TotalMilliseconds, false);
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
    private async Task RunAndShowAsync(string extension, ExtensionButton button, string command)
    {
        var (result, _) = await ExecuteAsync(extension, button, command, CommandSources.Ribbon).ConfigureAwait(false);

        // A failure was shown by the pipeline already (ribbon source)
        if (result.Error is null && result.Value is { } value && Describe(value) is { Length: > 0 } text)
        {
            notifier.Info(ButtonUsageStore.Normalize(button.Name), text.Length <= MaxResultLength ? text : text[..MaxResultLength] + "\n…");
        }
    }

    private async Task<(CommandResult Result, TimeSpan Elapsed)> ExecuteAsync(string extension, ExtensionButton button, string command, string source)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(new CommandRequest(command, button.Args ?? default, source)).ConfigureAwait(false);
        usage.RecordResult(extension, button.Name, result.Error is null, stopwatch.Elapsed, result.Error?.Message);
        return (result, stopwatch.Elapsed);
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
