using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Extensions.Testing;

/// <summary>How one model test has run.</summary>
/// <param name="TestId">The full id of the test.</param>
/// <param name="Test">Its name at the last run.</param>
/// <param name="Suite">The name of its suite at the last run.</param>
/// <param name="ExtensionId">The folder (extension) it lives in.</param>
/// <param name="Runs">How many times it ran (skipped runs not counted).</param>
/// <param name="Failed">Runs with new failures.</param>
/// <param name="Errors">Runs in which the test itself threw: a bug in the test.</param>
/// <param name="Inconclusive">Runs in which Revit could not tell.</param>
/// <param name="LastStatus">The status of the last run.</param>
/// <param name="LastRun">When it ran last.</param>
/// <param name="LastNew">New failures of the last run.</param>
/// <param name="LastError">The message of the last error of the test (it threw).</param>
/// <param name="TotalMilliseconds">The time all runs took.</param>
/// <param name="Days">Runs per day (<c>yyyy-MM-dd</c>, local time), the last <see cref="ModelTestUsageStore.DaysKept" /> days.</param>
public sealed record ModelTestUsage(
    string TestId,
    string Test,
    string Suite,
    string ExtensionId,
    int Runs,
    int Failed,
    int Errors,
    int Inconclusive,
    string? LastStatus,
    DateTimeOffset? LastRun,
    int LastNew,
    string? LastError,
    long TotalMilliseconds,
    IReadOnlyDictionary<string, int> Days)
{
    /// <summary>The average time of a run.</summary>
    public double? AverageMilliseconds => Runs == 0 ? null : (double)TotalMilliseconds / Runs;
}

/// <summary>Everything recorded about the model tests.</summary>
/// <param name="Since">The first run counted, or the last reset.</param>
/// <param name="Runs">Runs of tests (one click of Run all is one run of many tests).</param>
/// <param name="Tests">Every test that ran, most run first.</param>
public sealed record ModelTestUsageSnapshot(DateTimeOffset? Since, int Runs, IReadOnlyList<ModelTestUsage> Tests);

/// <summary>
///     Counts the runs of the model tests — from the pane, a window, Watch or an AI client — in
///     <c>%LOCALAPPDATA%\AnalyseTool\model-tests\usage.json</c>: the Model tests tab of the statistics. Local only.
/// </summary>
/// <remarks>Thread-safe; written once per run.</remarks>
public sealed class ModelTestUsageStore
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
    public ModelTestUsageStore(ExtensionPaths paths, TimeProvider? time = null)
    {
        FilePath = Path.Combine(paths.ProfileRoot, "model-tests", "usage.json");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Gets the file the counts live in.</summary>
    public string FilePath { get; }

    /// <summary>Counts a run: every test of it that was not skipped.</summary>
    /// <param name="run">The run.</param>
    /// <param name="names">The test and suite names by test id, as listed at the run.</param>
    internal void Record(ModelTestRun run, IReadOnlyDictionary<string, (string Test, string Suite, string ExtensionId)> names)
    {
        var ran = run.Results.Where(result => result.Status != "skipped").ToList();
        if (ran.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var data = Load();
            var now = _time.GetLocalNow();
            var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            data.Since ??= now;
            data.Runs++;
            foreach (var result in ran)
            {
                if (!data.Tests.TryGetValue(result.TestId, out var entry))
                {
                    entry = data.Tests[result.TestId] = new Entry { TestId = result.TestId };
                }

                if (names.TryGetValue(result.TestId, out var name))
                {
                    (entry.Test, entry.Suite, entry.ExtensionId) = name;
                }

                entry.Runs++;
                entry.Failed += result.Status == "failed" ? 1 : 0;
                entry.Errors += result.Status == "error" ? 1 : 0;
                entry.Inconclusive += result.Status == "inconclusive" ? 1 : 0;
                entry.LastStatus = result.Status;
                entry.LastRun = now;
                entry.LastNew = result.New;
                entry.TotalMilliseconds += result.Milliseconds;
                if (result.Errors > 0 && result.Cases.FirstOrDefault(item => item.Status == "error") is { } error)
                {
                    entry.LastError = error.Message;
                }

                entry.Days[day] = entry.Days.GetValueOrDefault(day) + 1;
                var oldest = now.AddDays(-(DaysKept - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                foreach (var old in entry.Days.Keys.Where(key => string.CompareOrdinal(key, oldest) < 0).ToList())
                {
                    entry.Days.Remove(old);
                }
            }

            Save(data);
        }
    }

    /// <summary>Everything recorded, most run first.</summary>
    public ModelTestUsageSnapshot Read()
    {
        lock (_gate)
        {
            var data = Load();
            var tests = data.Tests.Values
                .Select(entry => new ModelTestUsage(entry.TestId, entry.Test ?? entry.TestId, entry.Suite ?? "", entry.ExtensionId ?? "", entry.Runs, entry.Failed,
                    entry.Errors, entry.Inconclusive, entry.LastStatus, entry.LastRun, entry.LastNew, entry.LastError, entry.TotalMilliseconds,
                    new SortedDictionary<string, int>(entry.Days, StringComparer.Ordinal)))
                .OrderByDescending(usage => usage.Runs)
                .ThenBy(usage => usage.TestId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new ModelTestUsageSnapshot(data.Since, data.Runs, tests);
        }
    }

    /// <summary>Forgets every count.</summary>
    /// <returns>How many tests were forgotten.</returns>
    public int Reset()
    {
        lock (_gate)
        {
            var data = Load();
            var count = data.Tests.Count;
            _data = new Data();
            Save(_data);
            return count;
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
            _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Options) ?? new Data() : new Data();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // A broken file starts the counting afresh rather than failing every run
            _data = new Data();
        }

        _data.Tests = new Dictionary<string, Entry>(_data.Tests, StringComparer.OrdinalIgnoreCase);
        return _data;
    }

    private void Save(Data data)
    {
        try
        {
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch (IOException)
        {
            // Statistics are a nicety: a held file costs a count, not the run
        }
    }

    private sealed class Data
    {
        public DateTimeOffset? Since { get; set; }

        public int Runs { get; set; }

        public Dictionary<string, Entry> Tests { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Entry
    {
        public string TestId { get; set; } = "";

        public string? Test { get; set; }

        public string? Suite { get; set; }

        public string? ExtensionId { get; set; }

        public int Runs { get; set; }

        public int Failed { get; set; }

        public int Errors { get; set; }

        public int Inconclusive { get; set; }

        public string? LastStatus { get; set; }

        public DateTimeOffset? LastRun { get; set; }

        public int LastNew { get; set; }

        public string? LastError { get; set; }

        public long TotalMilliseconds { get; set; }

        public Dictionary<string, int> Days { get; set; } = new(StringComparer.Ordinal);
    }
}
