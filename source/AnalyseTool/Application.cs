using AnalyseTool.Commands;
using AnalyseTool.Infrastructure.Extensions;
using AnalyseTool.Infrastructure.Ribbon;
using AnalyseTool.Resources;
using Autodesk.Revit.UI;
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
    // Segoe MDL2 Assets
    private const string GlyphDock = "";
    private const string GlyphNew = "";
    private const string GlyphManage = "";
    private const string GlyphSettings = "";
    private const string GlyphAbout = "";
    private const string GlyphBug = "";

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
        var main = Application.CreatePanel("AnalyseTool", "AnalyseTool");
        main.AddButton<AnalyseToolCommand>("AnalyseTool")
            .WithIcons("RibbonIcon");
        main.AddButton<DockCommand>("Buttons\npane")
            .WithGlyph(GlyphDock);

        var buttons = Application.CreatePanel("Buttons", "AnalyseTool");
        buttons.AddButton<NewButtonCommand>("New\nbutton")
            .WithGlyph(GlyphNew);
        buttons.AddButton<ExtensionsCommand>("Manage\nbuttons")
            .WithGlyph(GlyphManage);

        var help = Application.CreatePanel("Help", "AnalyseTool");
        var stack = help.AddStackedItems(
            RibbonButtons.CreateButtonData<SettingsCommand>("Settings"),
            RibbonButtons.CreateButtonData<AboutCommand>("About"),
            RibbonButtons.CreateButtonData<ReportBugCommand>("Report a bug"));
        string[] glyphs = [GlyphSettings, GlyphAbout, GlyphBug];
        for (var index = 0; index < stack.Count; index++)
        {
            if (stack[index] is PushButton button)
            {
                button.Image = Icons.Glyph(glyphs[index], 16);
            }
        }

        // The buttons of the user's extensions: added, changed and removed live (AdWindows), after the Revit ribbon is built
        Host.GetService<ExtensionRibbon>().Start();

#if DEBUG
        var devPanel = Application.CreatePanel("Dev", "AnalyseTool");

        devPanel.AddButton<StatusCommand>("Status")
            .WithIcons("RibbonIcon");

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
