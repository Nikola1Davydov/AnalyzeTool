using AnalyseTool.Infrastructure.Ribbon;
using AnalyseTool.Commands;
using AnalyseTool.Resources;
using Nice3point.Revit.Extensions.UI;
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
        CreateRibbon();
    }

    /// <inheritdoc />
    public override async Task OnShutdownAsync()
    {
        await Host.StopAsync();
    }

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("General", "AnalyseTool");

        panel.AddButton<StatusCommand>("Status")
            .WithIcons("RibbonIcon");

#if DEBUG
        var devPanel = Application.CreatePanel("Dev", "AnalyseTool");

        devPanel.AddButton<ActiveDocumentCommand>("Active\ndocument")
            .SetAvailability<DocumentAvailability>()
            .WithIcons("RibbonIcon");

        devPanel.AddButton<UserSettingsCommand>("User\nsettings")
            .WithIcons("RibbonIcon");

        devPanel.AddButton<DevPageCommand>("Dev\npage")
            .WithIcons("RibbonIcon");
#endif
    }
}
