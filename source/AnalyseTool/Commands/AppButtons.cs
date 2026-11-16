using System.Diagnostics;
using AnalyseTool.Hosting.WebView.Controls;
using AnalyseTool.Infrastructure.App;
using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Commands;

// The ribbon buttons of AnalyseTool itself. Each class keeps its name and namespace (Revit binds shortcuts to them) and has
// a same-named proxy line in the Launcher's Proxies.cs.

/// <summary>Opens the main window: the parameter report and the shared parameters.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class AnalyseToolCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        AppWindows.Open(Host.GetService<IWindows>(), "/");
    }
}

/// <summary>Shows the dock pane: the user's buttons at hand.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class DockCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<IWindows>().ShowDockPane("/dock");
    }
}

/// <summary>Opens the extension manager: the user's buttons, their versions, their errors.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ExtensionsCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        AppWindows.Open(Host.GetService<IWindows>(), "/extensions");
    }
}

/// <summary>Creates a new button: C# sources AnalyseTool compiles, with their own version history.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class NewButtonCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        AppWindows.Open(Host.GetService<IWindows>(), "/extensions/new");
    }
}

/// <summary>The settings: MCP and the folders of extensions.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class SettingsCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        AppWindows.Open(Host.GetService<IWindows>(), "/settings");
    }
}

/// <summary>The version and what is new.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class AboutCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        AppWindows.Open(Host.GetService<IWindows>(), "/about");
    }
}

/// <summary>Opens the issues of the repository in the browser.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ReportBugCommand : ExternalCommand
{
    /// <summary>Where bugs and wishes go.</summary>
    public const string IssuesUrl = "https://github.com/Nikola1Davydov/AnalyzeTool/issues";

    /// <inheritdoc />
    public override void Execute()
    {
        Process.Start(new ProcessStartInfo(IssuesUrl) { UseShellExecute = true });
    }
}
