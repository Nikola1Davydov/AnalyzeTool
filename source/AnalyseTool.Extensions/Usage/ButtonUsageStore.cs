using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Extensions.Usage;

/// <summary>How one ribbon button has been used.</summary>
/// <param name="Extension">The id of the extension; <c>analysetool</c> for the buttons of AnalyseTool itself.</param>
/// <param name="Button">The text of the button, line breaks as spaces.</param>
/// <param name="Command">The command its last click ran; <see langword="null" /> for a button that opens a page.</param>
/// <param name="Clicks">Every click.</param>
/// <param name="Failures">Clicks whose command failed.</param>
/// <param name="FirstClick">The first click.</param>
/// <param name="LastClick">The last click.</param>
/// <param name="LastError">The message of the last failure.</param>
/// <param name="LastErrorAt">When it failed.</param>
/// <param name="TotalMilliseconds">The time all timed runs of the command took.</param>
/// <param name="TimedRuns">The runs counted in <paramref name="TotalMilliseconds" />.</param>
/// <param name="Days">Clicks per day (<c>yyyy-MM-dd</c>, local time), the last <see cref="ButtonUsageStore.DaysKept" /> days.</param>
public sealed record ButtonUsage(
    string Extension,
    string Button,
    string? Command,
    int Clicks,
    int Failures,
    DateTimeOffset? FirstClick,
    DateTimeOffset? LastClick,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    long TotalMilliseconds,
    int TimedRuns,
    IReadOnlyDictionary<string, int> Days)
{
    /// <summary>The last run of its command failed (and none succeeded since).</summary>
    public bool LastRunFailed { get; init; }

    /// <summary>The average time of a run; <see langword="null" /> when none was timed.</summary>
    public double? AverageMilliseconds => TimedRuns == 0 ? null : (double)TotalMilliseconds / TimedRuns;
}

/// <summary>Everything recorded.</summary>
/// <param name="Since">When the counting started: the first click, or the last reset.</param>
/// <param name="Buttons">Every button clicked at least once since.</param>
public sealed record ButtonUsageSnapshot(DateTimeOffset? Since, IReadOnlyList<ButtonUsage> Buttons);

/// <summary>
///     Counts the clicks of every ribbon button — of AnalyseTool and of the extensions alike — in
///     <c>%LOCALAPPDATA%\AnalyseTool\button-usage.json</c>. Local only: nothing leaves the machine.
/// </summary>
/// <remarks>Thread-safe. A click is written at once (atomically): clicks are rare, and a crash of Revit keeps them.</remarks>
public sealed class ButtonUsageStore
{
    /// <summary>How many days of daily counts are kept.</summary>
    public const int DaysKept = 90;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private Data? _data;

    /// <summary>Creates the store in the profile of <paramref name="paths" />.</summary>
    public ButtonUsageStore(ExtensionPaths paths, TimeProvider? time = null)
    {
        FilePath = Path.Combine(paths.ProfileRoot, "button-usage.json");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Gets the file the counts live in.</summary>
    public string FilePath { get; }

    /// <summary>The key of a button: the extension id and the button text.</summary>
    public static string Key(string extension, string button)
    {
        return extension + "/" + Normalize(button);
    }

    /// <summary>The text of a button as statistics show it: line breaks become spaces.</summary>
    public static string Normalize(string button)
    {
        return string.Join(' ', button.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Counts a click.</summary>
    public void RecordClick(string extension, string button, string? command)
    {
        lock (_gate)
        {
            var data = Load();
            var now = _time.GetLocalNow();
            var entry = Get(data, extension, button);
            entry.Command = command;
            entry.Clicks++;
            entry.FirstClick ??= now;
            entry.LastClick = now;
            var day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            entry.Days[day] = entry.Days.GetValueOrDefault(day) + 1;
            Trim(entry, now);
            data.Since ??= now;
            Save(data);
        }
    }

    /// <summary>Records how the command of a click ended.</summary>
    public void RecordResult(string extension, string button, bool success, TimeSpan duration, string? error)
    {
        lock (_gate)
        {
            var data = Load();
            var entry = Get(data, extension, button);
            entry.TotalMilliseconds += (long)duration.TotalMilliseconds;
            entry.TimedRuns++;
            entry.LastRunFailed = !success;
            if (!success)
            {
                entry.Failures++;
                entry.LastError = error;
                entry.LastErrorAt = _time.GetLocalNow();
            }

            Save(data);
        }
    }

    /// <summary>Everything recorded, most clicked first.</summary>
    public ButtonUsageSnapshot Read()
    {
        lock (_gate)
        {
            var data = Load();
            var buttons = data.Buttons.Values
                .Select(entry => new ButtonUsage(entry.Extension, entry.Button, entry.Command, entry.Clicks, entry.Failures, entry.FirstClick, entry.LastClick,
                    entry.LastError, entry.LastErrorAt, entry.TotalMilliseconds, entry.TimedRuns, new SortedDictionary<string, int>(entry.Days, StringComparer.Ordinal))
                {
                    LastRunFailed = entry.LastRunFailed
                })
                .OrderByDescending(usage => usage.Clicks)
                .ThenBy(usage => usage.Extension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(usage => usage.Button, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new ButtonUsageSnapshot(data.Since, buttons);
        }
    }

    /// <summary>Forgets the counts of one extension, or of every button.</summary>
    /// <returns>How many buttons were forgotten.</returns>
    public int Reset(string? extension = null)
    {
        lock (_gate)
        {
            var data = Load();
            var keys = data.Buttons.Where(pair => extension is null || string.Equals(pair.Value.Extension, extension, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in keys)
            {
                data.Buttons.Remove(key);
            }

            if (extension is null)
            {
                data.Since = null;
            }

            Save(data);
            return keys.Count;
        }
    }

    private static Entry Get(Data data, string extension, string button)
    {
        var key = Key(extension, button);
        if (!data.Buttons.TryGetValue(key, out var entry))
        {
            entry = data.Buttons[key] = new Entry { Extension = extension, Button = Normalize(button) };
        }

        return entry;
    }

    private static void Trim(Entry entry, DateTimeOffset now)
    {
        var oldest = now.AddDays(-(DaysKept - 1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var day in entry.Days.Keys.Where(day => string.CompareOrdinal(day, oldest) < 0).ToList())
        {
            entry.Days.Remove(day);
        }
    }

    private Data Load()
    {
        if (_data is not null)
        {
            return _data;
        }

        try
        {
            _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken file must not break the buttons: keep it aside and count anew
            TryMoveAside();
            _data = null;
        }

        _data ??= new Data();
        _data.Buttons = new Dictionary<string, Entry>(_data.Buttons ?? [], StringComparer.OrdinalIgnoreCase);
        return _data;
    }

    private void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Statistics are never worth a failed click; the counts stay in memory until the next write succeeds
        }
    }

    private void TryMoveAside()
    {
        try
        {
            File.Move(FilePath, FilePath + ".bad", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Data
    {
        public DateTimeOffset? Since { get; set; }

        public Dictionary<string, Entry> Buttons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Entry
    {
        public string Extension { get; set; } = "";

        public string Button { get; set; } = "";

        public string? Command { get; set; }

        public int Clicks { get; set; }

        public int Failures { get; set; }

        public DateTimeOffset? FirstClick { get; set; }

        public DateTimeOffset? LastClick { get; set; }

        public string? LastError { get; set; }

        public DateTimeOffset? LastErrorAt { get; set; }

        public long TotalMilliseconds { get; set; }

        public int TimedRuns { get; set; }

        public bool LastRunFailed { get; set; }

        public Dictionary<string, int> Days { get; set; } = new(StringComparer.Ordinal);
    }
}
