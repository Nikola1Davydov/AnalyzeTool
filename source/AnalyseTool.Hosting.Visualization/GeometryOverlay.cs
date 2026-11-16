using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Visualization.Rendering;

namespace AnalyseTool.Hosting.Visualization;

/// <summary>
///     Shows geometry through one DirectContext3D server, registered at the first <see cref="Show" />.
/// </summary>
/// <remarks>
///     The server stays registered until Revit closes: Revit does not allow removing a server that has drawn in an open
///     document. With nothing to show it declines every view.
/// </remarks>
internal sealed class GeometryOverlay : IGeometryOverlay
{
    private readonly OverlayServer _server = new();

    /// <inheritdoc />
    public int Count => _server.Count;

    /// <inheritdoc />
    public void Show(Document document, OverlayGeometry geometry)
    {
        if (geometry.IsEmpty)
        {
            return;
        }

        Register();
        _server.Add(document, geometry);
        Redraw(document.Application);
    }

    /// <inheritdoc />
    public int Clear()
    {
        var application = _server.Application;
        var count = _server.Clear();
        if (application is not null)
        {
            Redraw(application);
        }

        return count;
    }

    private void Register()
    {
        var service = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
        if (service.IsRegisteredServerId(OverlayServer.Id))
        {
            return;
        }

        service.AddServer(_server);
        var active = service.GetActiveServerIds();
        active.Add(OverlayServer.Id);
        service.SetActiveServers(active);
    }

    // The views of other documents are redrawn when the user switches to them
    private static void Redraw(Autodesk.Revit.ApplicationServices.Application application)
    {
        new UIApplication(application).ActiveUIDocument?.RefreshActiveView();
    }
}
