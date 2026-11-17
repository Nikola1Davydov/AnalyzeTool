using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using AnalyseTool.Services.Updates;

namespace AnalyseTool.Tests.Updates;

/// <summary>The daily check for a new AnalyseTool: GitHub at most once a day, silent without a network.</summary>
public sealed class UpdateCheckTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NewerRelease_IsAvailable_WithItsPage()
    {
        var github = new FakeGitHub("""{ "tag_name": "2.1.0", "html_url": "https://github.com/Nikola1Davydov/AnalyzeTool/releases/tag/2.1.0" }""");
        var check = new UpdateCheck(NewProfile(), github, new ManualTime(Monday));

        var info = await check.GetAsync("2.0.1", CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(info.Available).IsTrue();
            await Assert.That(info.Latest).IsEqualTo("2.1.0");
            await Assert.That(info.Url).IsEqualTo("https://github.com/Nikola1Davydov/AnalyzeTool/releases/tag/2.1.0");
            await Assert.That(github.Requests).IsEqualTo(1);
            await Assert.That(github.UserAgent).IsEqualTo("AnalyseTool");
        }
    }

    [Test]
    public async Task SameOrOlderRelease_IsNotAvailable()
    {
        var check = new UpdateCheck(NewProfile(), new FakeGitHub("""{ "tag_name": "v2.0.0", "html_url": "x" }"""), new ManualTime(Monday));

        var info = await check.GetAsync("2.0.0", CancellationToken.None);

        await Assert.That(info.Available).IsFalse();
        await Assert.That(info.Latest).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task WithinADay_AsksGitHubOnce_ForEveryRevit()
    {
        var profile = NewProfile();
        var github = new FakeGitHub("""{ "tag_name": "2.1.0", "html_url": "x" }""");
        var time = new ManualTime(Monday);

        await new UpdateCheck(profile, github, time).GetAsync("2.0.1", CancellationToken.None);
        time.Now = Monday.AddHours(23);
        // Another Revit version: its own instance, the same profile
        var again = await new UpdateCheck(profile, github, time).GetAsync("2.0.1", CancellationToken.None);
        await Assert.That(github.Requests).IsEqualTo(1);
        await Assert.That(again.Available).IsTrue();

        time.Now = Monday.AddDays(1).AddMinutes(1);
        await new UpdateCheck(profile, github, time).GetAsync("2.0.1", CancellationToken.None);
        await Assert.That(github.Requests).IsEqualTo(2);
    }

    [Test]
    public async Task Offline_ShowsNothing_AndWaitsADay()
    {
        var github = new FakeGitHub(null);
        var time = new ManualTime(Monday);
        var check = new UpdateCheck(NewProfile(), github, time);

        var info = await check.GetAsync("2.0.1", CancellationToken.None);
        time.Now = Monday.AddHours(2);
        await check.GetAsync("2.0.1", CancellationToken.None);

        await Assert.That(info.Available).IsFalse();
        await Assert.That(info.Latest).IsNull();
        await Assert.That(github.Requests).IsEqualTo(1);
    }

    [Test]
    [Arguments("2.1.0", "2.0.1", true)]
    [Arguments("v2.0.10", "2.0.9", true)]
    [Arguments("2.0.1", "2.0.1", false)]
    [Arguments("2.0.0", "2.0.1-beta.1", false)]
    [Arguments("1.6.0", "2.0.0", false)]
    [Arguments("not a version", "2.0.0", false)]
    public async Task IsNewer_ComparesTheVersionNumbers(string latest, string current, bool newer)
    {
        await Assert.That(UpdateCheck.IsNewer(latest, current)).IsEqualTo(newer);
    }

    private static string NewProfile()
    {
        return Path.Combine(Path.GetTempPath(), $"at-update-{Guid.NewGuid():N}");
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>GitHub's latest-release endpoint; <see langword="null" /> answers as a machine without a network.</summary>
    private sealed class FakeGitHub(string? release) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            UserAgent = request.Headers.UserAgent.ToString();
            if (release is null)
            {
                throw new HttpRequestException("No network");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release, Encoding.UTF8, "application/json") });
        }
    }
}
