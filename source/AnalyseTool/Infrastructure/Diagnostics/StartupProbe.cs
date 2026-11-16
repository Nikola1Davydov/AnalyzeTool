#if DEBUG
using AnalyseTool.Hosting.Lifetime;
using AnalyseTool.Hosting.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Infrastructure.Diagnostics;

/// <summary>
///     Debug-only startup check: a background service with ordinary <c>await</c>s waits for Revit to
///     initialize, then reads the model through <see cref="IRevitDispatcher" />. If Revit starts normally, startup did not hang.
/// </summary>
internal sealed partial class StartupProbe(IRevitLifetime revitLifetime, IRevitDispatcher revit, ILogger<StartupProbe> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await revitLifetime.Initialized.WaitAsync(stoppingToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);   // an ordinary await without ConfigureAwait(false)

        var (version, documents) = await revit.InvokeAsync(application =>
            (application.Application.VersionNumber, application.Application.Documents.Size), stoppingToken);

        LogRevitReady(logger, version, documents);
    }

    [LoggerMessage(LogLevel.Information, "Revit {Version} initialized; open documents: {Documents}")]
    private static partial void LogRevitReady(ILogger logger, string version, int documents);
}
#endif
