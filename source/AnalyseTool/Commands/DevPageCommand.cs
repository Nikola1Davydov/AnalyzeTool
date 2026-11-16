#if DEBUG
using Autodesk.Revit.Attributes;
using AnalyseTool.Hosting.WebView.Controls;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Commands;

/// <summary>
///     Debug-only button: shows the «Dev» page in the dock pane — the live log and a form that runs any registered command.
///     The pane is one per add-in: the page takes the place of the pane's own route (<c>/dock</c>) until Revit restarts.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class DevPageCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<IWindows>().ShowDockPane("/dev");
    }
}
#endif
