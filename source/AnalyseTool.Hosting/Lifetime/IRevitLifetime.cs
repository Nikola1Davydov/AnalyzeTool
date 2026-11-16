using Autodesk.Revit.ApplicationServices;

namespace AnalyseTool.Hosting.Lifetime;

/// <summary>
///     Revit readiness on top of the standard <c>IHostApplicationLifetime</c>.
/// </summary>
/// <remarks>
///     <c>IHostApplicationLifetime.ApplicationStarted</c> keeps its usual meaning — the host has started, still inside
///     <c>OnStartup</c>. Revit raises <c>ApplicationInitialized</c> only after <c>OnStartup</c> returns, and waiting for it
///     inside <c>OnStartup</c> would hang Revit, so readiness is a separate signal.
/// </remarks>
/// <example>
///     <code>
///     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
///     {
///         await revitLifetime.Initialized.WaitAsync(stoppingToken);
///         // the Revit API is reachable through IRevitDispatcher from here on
///     }
///     </code>
/// </example>
[PublicAPI]
public interface IRevitLifetime
{
    /// <summary>Gets a task that completes when Revit raises <c>ApplicationInitialized</c>.</summary>
    Task Initialized { get; }

    /// <summary>Gets a value indicating whether Revit has raised <c>ApplicationInitialized</c>.</summary>
    bool IsInitialized { get; }
}

/// <summary>
///     Completes <see cref="Initialized" /> on <see cref="ControlledApplication.ApplicationInitialized" />.
/// </summary>
internal sealed class RevitLifetime : IRevitLifetime, IDisposable
{
    private readonly ControlledApplication _application;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Subscribes to the event. Must be created during add-in startup, before Revit finishes initializing.</summary>
    public RevitLifetime(ControlledApplication application)
    {
        _application = application;
        _application.ApplicationInitialized += OnApplicationInitialized;
    }

    /// <inheritdoc />
    public Task Initialized => _initialized.Task;

    /// <inheritdoc />
    public bool IsInitialized => _initialized.Task.IsCompleted;

    /// <inheritdoc />
    public void Dispose()
    {
        _application.ApplicationInitialized -= OnApplicationInitialized;
    }

    private void OnApplicationInitialized(object? sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs args)
    {
        _initialized.TrySetResult();
    }
}
