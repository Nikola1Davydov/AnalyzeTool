using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalyseTool.Extensions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnalyseTool.Services.Updates;

/// <summary>Whether a newer AnalyseTool is released.</summary>
/// <param name="Current">The installed version.</param>
/// <param name="Latest">The latest release, when known.</param>
/// <param name="Url">Its page on GitHub, with the installers.</param>
/// <param name="Available">Whether <paramref name="Latest" /> is newer than <paramref name="Current" />.</param>
/// <param name="CheckedAt">When GitHub was asked last.</param>
public sealed record UpdateInfo(string Current, string? Latest, string? Url, bool Available, DateTimeOffset? CheckedAt);

/// <summary>
///     Asks GitHub for the latest release of AnalyseTool at most once a day, for every Revit version together: the answer
///     is kept in <c>update-check.json</c> of the profile. The request carries nothing but the program name; without a
///     network nothing happens and nothing is shown.
/// </summary>
public sealed class UpdateCheck(string profileRoot, HttpMessageHandler handler, TimeProvider time, ILogger<UpdateCheck>? logger = null)
{
    /// <summary>The latest release; GitHub leaves pre-releases out.</summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/Nikola1Davydov/AnalyzeTool/releases/latest";

    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger = logger ?? NullLogger<UpdateCheck>.Instance;

    private string CacheFile => Path.Combine(profileRoot, "update-check.json");

    /// <summary>
    ///     Returns whether a newer version is released: from the last answer when it is younger than a day, otherwise asks
    ///     GitHub.
    /// </summary>
    public async Task<UpdateInfo> GetAsync(string current, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = ReadCache();
            var now = time.GetUtcNow();
            if (cached is null || now - cached.CheckedAt >= Interval || cached.CheckedAt > now)
            {
                cached = await FetchAsync(cached, now, cancellationToken).ConfigureAwait(false);
            }

            var available = cached.Latest is { } latest && IsNewer(latest, current);
            return new UpdateInfo(current, cached.Latest, cached.Url, available, cached.CheckedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Whether <paramref name="latest" /> («2.1.0», «v2.1.0») is a newer release than <paramref name="current" />.</summary>
    public static bool IsNewer(string latest, string current)
    {
        return Parse(latest) is { } a && Parse(current) is { } b && a > b;
    }

    private static Version? Parse(string version)
    {
        var core = version.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var parsed) ? new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0)) : null;
    }

    private async Task<Cache> FetchAsync(Cache? previous, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A failed attempt counts too: no new request every time the pane opens while offline
        var result = previous is null ? new Cache(now, null, null) : previous with { CheckedAt = now };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AnalyseTool", null));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var release = await JsonSerializer.DeserializeAsync<Release>(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), Json, cancellationToken).ConfigureAwait(false);
                if (release?.TagName is { Length: > 0 } tag)
                {
                    result = new Cache(now, tag.TrimStart('v', 'V'), release.HtmlUrl);
                }
            }
            else
            {
                LogFailed(_logger, $"GitHub answered {(int)response.StatusCode}");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(_logger, exception.Message);
        }

        try
        {
            Directory.CreateDirectory(profileRoot);
            AtomicFile.WriteAllText(CacheFile, JsonSerializer.Serialize(result, Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, exception.Message);
        }

        return result;
    }

    private Cache? ReadCache()
    {
        try
        {
            return File.Exists(CacheFile) ? JsonSerializer.Deserialize<Cache>(File.ReadAllText(CacheFile), Json) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void LogFailed(ILogger logger, string reason)
    {
        logger.LogDebug("The check for a new version of AnalyseTool did not get an answer: {Reason}", reason);
    }

    private sealed record Cache(DateTimeOffset CheckedAt, string? Latest, string? Url);

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
