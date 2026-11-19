using AnalyseTool.Infrastructure.Ribbon;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool;

/// <summary>
///     Provides the Revit application entry point.
/// </summary>
/// <remarks>
///     Revit loads AnalyseTool.Launcher, which creates this class inside its isolated AssemblyLoadContext.
///     An exception here reaches the Launcher: it writes the startup log and lets Revit report the failure.
/// </remarks>
[UsedImplicitly]
public class Application : AsyncExternalApplication
{
    /// <inheritdoc />
    public override async Task OnStartupAsync()
    {
        await Host.StartAsync(Application);

        // Every button — AnalyseTool's own (Ribbon\analysetool\plugin.json) and the extensions' — is declared in a plugin.json
        // and built by the same code with AdWindows; a click runs a pipeline command or opens a page (ButtonRunner)
        Host.GetService<AppRibbon>().Start();
    }

    /// <inheritdoc />
    public override async Task OnShutdownAsync()
    {
        await Host.StopAsync();
    }
}
