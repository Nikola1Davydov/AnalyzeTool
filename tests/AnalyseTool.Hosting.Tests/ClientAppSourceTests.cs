using AnalyseTool.Hosting.WebView.ClientApp;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.Hosting.Tests;

/// <summary>Where the pages come from and which origin may call commands (plan, section 9).</summary>
public sealed class ClientAppSourceTests
{
    [Test]
    public async Task Development_LoadsTheDevServerWithAHashRoute()
    {
        var source = Create(Environments.Development);

        await Assert.That(source.UseDevServer).IsTrue();
        await Assert.That(source.GetUrl("/families")).IsEqualTo("http://localhost:5173/#/families");
    }

    [Test]
    public async Task Production_LoadsDistOverTheVirtualHost()
    {
        var source = Create(Environments.Production);

        await Assert.That(source.UseDevServer).IsFalse();
        await Assert.That(source.HostName).IsEqualTo("app.analysetool.local");
        await Assert.That(source.GetUrl("dock")).IsEqualTo("https://app.analysetool.local/index.html#/dock");
        await Assert.That(source.DistFolder).IsEqualTo(Path.Combine(@"C:\Addins\2026\AnalyseTool", "ClientApp", "dist"));
    }

    [Test]
    public async Task Development_WithUseDevServerOff_LoadsDist()
    {
        var source = Create(Environments.Development, new ClientAppOptions { UseDevServer = false });

        await Assert.That(source.Origin).IsEqualTo("https://app.analysetool.local");
    }

    [Test]
    [Arguments("https://app.analysetool.local/index.html#/", true)]
    [Arguments("HTTPS://APP.ANALYSETOOL.LOCAL/assets/index.js", true)]
    [Arguments("https://app.analysetool.local.evil.com/", false)]
    [Arguments("http://app.analysetool.local/", false)]
    [Arguments("https://example.com/changelog", false)]
    [Arguments("not a uri", false)]
    [Arguments(null, false)]
    public async Task IsOwn_AcceptsOnlyTheOrigin(string? uri, bool expected)
    {
        var source = Create(Environments.Production);

        await Assert.That(source.IsOwn(uri)).IsEqualTo(expected);
    }

    private static ClientAppSource Create(string environmentName, ClientAppOptions? options = null)
    {
        var environment = new TestEnvironment
        {
            EnvironmentName = environmentName,
            ApplicationName = "AnalyseTool",
            ContentRootPath = @"C:\Addins\2026\AnalyseTool"
        };

        return new ClientAppSource(Microsoft.Extensions.Options.Options.Create(options ?? new ClientAppOptions()), environment);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
