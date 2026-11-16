using System.ComponentModel;
using AnalyseTool.Extensions.Usage;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Infrastructure.Ribbon;

namespace AnalyseTool.Features.Statistics;

/// <summary>How one ribbon button is used.</summary>
/// <param name="Extension">The id of its folder; <c>analysetool</c> for AnalyseTool's own buttons.</param>
/// <param name="ExtensionName">The description of the folder, or its id.</param>
/// <param name="Button">The text of the button.</param>
/// <param name="Command">The command it runs; <see langword="null" /> when it opens a page.</param>
/// <param name="BuiltIn">One of AnalyseTool's own buttons.</param>
/// <param name="OnRibbon">It is on the ribbon now; <see langword="false" /> for a button of an extension that is off or gone.</param>
/// <param name="Clicks">Clicks since <see cref="ButtonStatisticsResult.Since" />.</param>
/// <param name="Failures">Clicks whose command failed.</param>
/// <param name="FirstClick">The first click.</param>
/// <param name="LastClick">The last click.</param>
/// <param name="LastError">The message of the last failure.</param>
/// <param name="LastErrorAt">When it failed.</param>
/// <param name="AverageMilliseconds">The average time its command took; <see langword="null" /> when it never ran.</param>
/// <param name="Last30Days">Clicks per day, the oldest first, today last.</param>
public sealed record ButtonStatistic(
    string Extension,
    string ExtensionName,
    string Button,
    string? Command,
    bool BuiltIn,
    bool OnRibbon,
    int Clicks,
    int Failures,
    DateTimeOffset? FirstClick,
    DateTimeOffset? LastClick,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    double? AverageMilliseconds,
    IReadOnlyList<int> Last30Days);

/// <summary>The statistics of every button.</summary>
/// <param name="Since">When the counting started: the first click, or the last reset.</param>
/// <param name="TotalClicks">All clicks.</param>
/// <param name="Buttons">Every button on the ribbon, and every button clicked before that is gone now; most clicked first.</param>
public sealed record ButtonStatisticsResult(DateTimeOffset? Since, int TotalClicks, IReadOnlyList<ButtonStatistic> Buttons);

/// <summary>Returns how every ribbon button is used.</summary>
[McpTool(ReadOnly = true, Title = "Button statistics")]
[Command(Description = "Returns how every ribbon button is used — AnalyseTool's own and the extensions': clicks, failures, last use, average run time and clicks per day of the last 30 days. Counted on this machine only.")]
public sealed class GetButtonStatistics(ButtonCatalog catalog, ButtonUsageStore usage, TimeProvider? time = null) : ICommand<ButtonStatisticsResult>
{
    /// <summary>The days in <see cref="ButtonStatistic.Last30Days" />.</summary>
    public const int Days = 30;

    /// <inheritdoc />
    public Task<ButtonStatisticsResult> ExecuteAsync(CommandContext context)
    {
        var snapshot = usage.Read();
        var today = DateOnly.FromDateTime((time ?? TimeProvider.System).GetLocalNow().DateTime);
        var recorded = snapshot.Buttons.ToDictionary(button => ButtonUsageStore.Key(button.Extension, button.Button), StringComparer.OrdinalIgnoreCase);

        var result = new List<ButtonStatistic>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.GetButtons())
        {
            var extension = entry.Source.Extension;
            var key = ButtonUsageStore.Key(extension.Id, entry.Button.Name);
            if (!seen.Add(key))
            {
                continue;
            }

            recorded.TryGetValue(key, out var used);
            var command = string.IsNullOrWhiteSpace(entry.Button.Command) ? null : entry.Button.Command;
            result.Add(Create(extension.Id, ButtonCatalog.FolderName(entry.Source), ButtonUsageStore.Normalize(entry.Button.Name), command, entry.Source.BuiltIn, entry.Source.Shown, used, today));
        }

        // Clicked once, gone now: a removed extension or a renamed button keeps its history until a reset
        foreach (var (key, used) in recorded)
        {
            if (!seen.Contains(key))
            {
                var builtIn = used.Extension.StartsWith(ButtonCatalog.BuiltInId, StringComparison.OrdinalIgnoreCase);
                result.Add(Create(used.Extension, used.Extension, used.Button, used.Command, builtIn, false, used, today));
            }
        }

        var ordered = result.OrderByDescending(button => button.Clicks)
            .ThenByDescending(button => button.OnRibbon)
            .ThenBy(button => button.BuiltIn ? 0 : 1)
            .ThenBy(button => button.Extension, StringComparer.OrdinalIgnoreCase)
            .ThenBy(button => button.Button, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(new ButtonStatisticsResult(snapshot.Since, ordered.Sum(button => button.Clicks), ordered));
    }

    private static ButtonStatistic Create(string extension, string name, string button, string? command, bool builtIn, bool onRibbon, ButtonUsage? used, DateOnly today)
    {
        var days = new int[Days];
        if (used is not null)
        {
            for (var index = 0; index < Days; index++)
            {
                var day = today.AddDays(index - (Days - 1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                days[index] = used.Days.GetValueOrDefault(day);
            }
        }

        return new ButtonStatistic(extension, name, button, command ?? used?.Command, builtIn, onRibbon, used?.Clicks ?? 0, used?.Failures ?? 0, used?.FirstClick, used?.LastClick,
            used?.LastError, used?.LastErrorAt, used?.AverageMilliseconds, days);
    }
}

/// <summary>Input of <see cref="ResetButtonStatistics" />.</summary>
public sealed record ResetButtonStatisticsRequest([property: Description("The extension whose counts to forget; every button when absent.")] string? Extension = null);

/// <summary>How many buttons were forgotten.</summary>
/// <param name="Removed">The buttons whose counts are gone.</param>
public sealed record ResetButtonStatisticsResult(int Removed);

/// <summary>Forgets the counts of one extension's buttons, or of all.</summary>
[Command(Description = "Forgets the click counts of one extension's buttons, or of every button.")]
public sealed class ResetButtonStatistics(ButtonUsageStore usage) : ICommand<ResetButtonStatisticsRequest, ResetButtonStatisticsResult>
{
    /// <inheritdoc />
    public Task<ResetButtonStatisticsResult> ExecuteAsync(ResetButtonStatisticsRequest request, CommandContext context)
    {
        return Task.FromResult(new ResetButtonStatisticsResult(usage.Reset(string.IsNullOrWhiteSpace(request.Extension) ? null : request.Extension)));
    }
}
