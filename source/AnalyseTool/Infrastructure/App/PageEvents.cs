using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Testing;
using AnalyseTool.Hosting.WebView.Transport;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.Infrastructure.App;

/// <summary>
///     Tells the open pages what changed, so they need not ask every few seconds: <c>revitThemeChanged</c> (payload
///     <see cref="RevitTheme" />) when the theme of Revit is switched, <c>buttonsChanged</c> when the extensions were
///     loaded again — a saved file, a new button, a restored version — or a button ran (<see cref="Ribbon.ButtonRunner" />),
///     <c>modelTestsRan</c> when model tests ran, from a page or an AI client.
/// </summary>
internal sealed class PageEvents(UIControlledApplication application, ExtensionHost extensions, ModelTestService tests, IWebViewEvents events) : IHostedService
{
    /// <summary>The theme of Revit was switched; the payload is the new <see cref="RevitTheme" />.</summary>
    public const string ThemeChanged = "revitThemeChanged";

    /// <summary>The buttons of the pane may have changed: ask <c>GetPaneButtons</c> again.</summary>
    public const string ButtonsChanged = "buttonsChanged";

    /// <summary>Model tests ran: ask <c>GetModelTestResults</c> for the run.</summary>
    public const string ModelTestsRan = "modelTestsRan";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        application.ThemeChanged += OnThemeChanged;
        extensions.Changed += OnExtensionsChanged;
        tests.Completed += OnTestsRan;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        application.ThemeChanged -= OnThemeChanged;
        extensions.Changed -= OnExtensionsChanged;
        tests.Completed -= OnTestsRan;
        return Task.CompletedTask;
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs args)
    {
        events.Publish(ThemeChanged, RevitTheme.Current());
    }

    private void OnExtensionsChanged(object? sender, EventArgs args)
    {
        events.Publish(ButtonsChanged);
    }

    private void OnTestsRan(object? sender, ModelTestRun run)
    {
        events.Publish(ModelTestsRan);
    }
}
