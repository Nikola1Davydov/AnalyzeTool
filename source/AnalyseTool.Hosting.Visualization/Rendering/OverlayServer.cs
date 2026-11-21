using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace AnalyseTool.Hosting.Visualization.Rendering;

/// <summary>
///     The DirectContext3D server of the overlay: Revit calls it while it draws a view and it draws the geometry of the
///     view's document.
/// </summary>
/// <remarks>
///     Every call comes on the Revit thread, as do <see cref="Add" /> and <see cref="Clear" />. The buffers are built at the
///     first draw of a geometry and kept until it is removed. A geometry whose document closed is dropped at the next call.
/// </remarks>
internal sealed class OverlayServer : IDirectContext3DServer
{
    /// <summary>The id of the server; each add-in has its own, so two add-ins of the template do not collide.</summary>
    public static readonly Guid Id = new("FC2940D0-D9BB-4F7F-93FF-65DD3BD334AF");

    private readonly List<Entry> _entries = [];

    /// <summary>How often Revit asked whether to draw, for the bounds, and drew: what tells a server Revit never calls.</summary>
    public (int CanExecute, int Bounds, int Render) Calls => (_canExecute, _bounds, _render);

    /// <summary>The last exception a call from Revit threw; Revit drops a server that throws without a word.</summary>
    public string? LastError { get; private set; }

    private int _canExecute;
    private int _bounds;
    private int _render;

    public int Count
    {
        get
        {
            Prune();
            return _entries.Count;
        }
    }

    /// <summary>The application of a shown document, to redraw its active view; <see langword="null" /> when nothing is shown.</summary>
    public Autodesk.Revit.ApplicationServices.Application? Application
    {
        get
        {
            Prune();
            return _entries.Count > 0 ? _entries[0].Document.Application : null;
        }
    }

    public void Add(Document document, OverlayGeometry geometry)
    {
        Prune();
        _entries.Add(new Entry(document, geometry));
    }

    public int Clear()
    {
        var count = _entries.Count;
        foreach (var entry in _entries)
        {
            entry.Dispose();
        }

        _entries.Clear();
        return count;
    }

    public Guid GetServerId() => Id;

    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

    public string GetName() => "Geometry overlay";

    // The module's own name, renamed with the add-in
    public string GetVendorId() => typeof(OverlayServer).Assembly.GetName().Name!;

    public string GetDescription() => "Geometry drawn over the views without changing the model";

    public string GetApplicationId() => "";

    public string GetSourceId() => "";

    public bool UsesHandles() => false;

    public bool CanExecute(View view)
    {
        _canExecute++;
        try
        {
            Prune();
            return Of(view).Any();
        }
        catch (Exception exception)
        {
            LastError = $"CanExecute: {exception}";
            return false;
        }
    }

    public Outline GetBoundingBox(View view)
    {
        _bounds++;
        try
        {
            return Bounds(view);
        }
        catch (Exception exception)
        {
            LastError = $"GetBoundingBox: {exception}";
            return new Outline(XYZ.Zero, XYZ.Zero);
        }
    }

    private Outline Bounds(View view)
    {
        var clamp = CutPlaneClamp.Of(view);
        Outline? bounds = null;
        foreach (var outline in Of(view).Select(entry => entry.Geometry.Bounds).OfType<Outline>())
        {
            var (min, max) = (clamp.Apply(outline.MinimumPoint), clamp.Apply(outline.MaximumPoint));
            if (bounds is null)
            {
                bounds = new Outline(min, max);
            }
            else
            {
                bounds.AddPoint(min);
                bounds.AddPoint(max);
            }
        }

        return bounds ?? new Outline(XYZ.Zero, XYZ.Zero);
    }

    public bool UseInTransparentPass(View view) => Of(view).Any(entry => entry.Geometry.Transparency > 0);

    /// <summary>
    ///     Revit calls this once for opaque and, when <see cref="UseInTransparentPass" /> says so, once more for transparent
    ///     geometry: faces go to the pass of their transparency, edges and curves to the opaque one. A plan gets the geometry
    ///     pressed onto its cut plane (<see cref="CutPlaneClamp" />).
    /// </summary>
    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        _render++;
        try
        {
            Render(view);
        }
        catch (Exception exception)
        {
            LastError = $"RenderScene: {exception}";
        }
    }

    private void Render(View view)
    {
        var transparentPass = DrawContext.IsTransparentPass();
        var clamp = CutPlaneClamp.Of(view);
        foreach (var entry in Of(view))
        {
            var (faces, lines) = entry.BuffersFor(clamp);
            if (entry.Geometry.Transparency > 0 == transparentPass)
            {
                faces?.Draw();
            }

            if (!transparentPass)
            {
                lines?.Draw();
            }
        }
    }

    private IEnumerable<Entry> Of(View view)
    {
        return _entries.Where(entry => entry.Document.Equals(view.Document));
    }

    private void Prune()
    {
        _entries.RemoveAll(entry =>
        {
            if (entry.Document.IsValidObject)
            {
                return false;
            }

            entry.Dispose();
            return true;
        });
    }

    private sealed class Entry(Document document, OverlayGeometry geometry) : IDisposable
    {
        // One set per clamp: the 3D views share one, every plan elevation has its own
        private readonly Dictionary<CutPlaneClamp, (OverlayBuffer? Faces, OverlayBuffer? Lines)> _buffers = [];

        public Document Document { get; } = document;

        public OverlayGeometry Geometry { get; } = geometry;

        public (OverlayBuffer? Faces, OverlayBuffer? Lines) BuffersFor(CutPlaneClamp clamp)
        {
            if (!_buffers.TryGetValue(clamp, out var buffers))
            {
                buffers = (Create(Geometry.Triangles, PrimitiveType.TriangleList, Geometry.Transparency), Create(Geometry.Lines, PrimitiveType.LineList, 0));
                _buffers[clamp] = buffers;
            }

            return buffers;

            OverlayBuffer? Create(IReadOnlyList<XYZ> points, PrimitiveType type, int transparency) =>
                points.Count == 0 ? null : new OverlayBuffer(clamp == CutPlaneClamp.None ? points : points.Select(clamp.Apply).ToList(), type, Geometry.Color, transparency / 100.0);
        }

        public void Dispose()
        {
            foreach (var (faces, lines) in _buffers.Values)
            {
                faces?.Dispose();
                lines?.Dispose();
            }

            _buffers.Clear();
        }
    }
}
