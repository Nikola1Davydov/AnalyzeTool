using AnalyseTool.Hosting.Visualization;
using AnalyseTool.Hosting.Visualization.Rendering;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
///     Revit geometry turned into the triangles and lines of the overlay. Needs the Revit API, not a document.
/// </summary>
public sealed class OverlayGeometryTests : RevitApiTest
{
    private static readonly Color Red = new(255, 0, 0);

    [Test]
    public async Task Add_Box_TwelveTrianglesAndTwelveEdges()
    {
        var geometry = new OverlayGeometry(Red).Add(new BoundingBoxXYZ { Min = XYZ.Zero, Max = new XYZ(1, 2, 3) });

        await Assert.That(geometry.Triangles.Count).IsEqualTo(12 * 3);
        await Assert.That(geometry.Lines.Count).IsEqualTo(12 * 2);
        await Assert.That(geometry.Bounds!.MaximumPoint.IsAlmostEqualTo(new XYZ(1, 2, 3))).IsTrue();
    }

    [Test]
    public async Task Add_BoxWithTransform_MovesTheCorners()
    {
        var box = new BoundingBoxXYZ { Min = XYZ.Zero, Max = new XYZ(1, 1, 1), Transform = Transform.CreateTranslation(new XYZ(10, 0, 0)) };

        var geometry = new OverlayGeometry(Red).Add(box);

        await Assert.That(geometry.Bounds!.MinimumPoint.IsAlmostEqualTo(new XYZ(10, 0, 0))).IsTrue();
    }

    [Test]
    public async Task Add_Solid_FacesAndEdges()
    {
        var square = CurveLoop.CreateViaThicken(Line.CreateBound(XYZ.Zero, new XYZ(1, 0, 0)), 1, XYZ.BasisZ);
        var cube = GeometryCreationUtilities.CreateExtrusionGeometry([square], XYZ.BasisZ, 1);

        var geometry = new OverlayGeometry(Red).Add(cube);

        await Assert.That(geometry.Triangles.Count).IsEqualTo(12 * 3);
        await Assert.That(geometry.Lines.Count).IsEqualTo(12 * 2);
    }

    [Test]
    public async Task Add_SolidWithOffset_MovesFacesAndEdgesOut()
    {
        var square = CurveLoop.CreateViaThicken(Line.CreateBound(XYZ.Zero, new XYZ(1, 0, 0)), 1, XYZ.BasisZ);
        var cube = GeometryCreationUtilities.CreateExtrusionGeometry([square], XYZ.BasisZ, 1);
        var bounds = cube.GetBoundingBox();
        var min = bounds.Transform.OfPoint(bounds.Min);

        var geometry = new OverlayGeometry(Red).Add(cube, offset: 0.1);

        await Assert.That(geometry.Bounds!.MinimumPoint.IsAlmostEqualTo(min - new XYZ(0.1, 0.1, 0.1))).IsTrue();
        // Every edge point left the cube along at least one axis
        await Assert.That(geometry.Lines.All(point => Outside(point.X, min.X) || Outside(point.Y, min.Y) || Outside(point.Z, min.Z))).IsTrue();

        static bool Outside(double value, double start) => value < start - 0.05 || value > start + 1.05;
    }

    [Test]
    public async Task Add_Point_CrossOfThreeLines()
    {
        var geometry = new OverlayGeometry(Red).Add(new XYZ(5, 5, 5), size: 2);

        await Assert.That(geometry.Triangles).IsEmpty();
        await Assert.That(geometry.Lines.Count).IsEqualTo(3 * 2);
        await Assert.That(geometry.Bounds!.MinimumPoint.IsAlmostEqualTo(new XYZ(4, 4, 4))).IsTrue();
    }

    [Test]
    public async Task New_Empty_HasNoBounds()
    {
        var geometry = new OverlayGeometry(Red, transparency: 150);

        await Assert.That(geometry.IsEmpty).IsTrue();
        await Assert.That(geometry.Bounds).IsNull();
        await Assert.That(geometry.Transparency).IsEqualTo(100);
    }

    [Test]
    public async Task CutPlaneClamp_FloorPlan_PressesWhatIsAboveUnderThePlane()
    {
        var clamp = new CutPlaneClamp(4, Below: true);

        await Assert.That(clamp.Apply(new XYZ(1, 2, 10)).IsAlmostEqualTo(new XYZ(1, 2, 4))).IsTrue();
        await Assert.That(clamp.Apply(new XYZ(1, 2, 3)).IsAlmostEqualTo(new XYZ(1, 2, 3))).IsTrue();
    }

    [Test]
    public async Task CutPlaneClamp_CeilingPlan_PressesWhatIsBelowAboveThePlane()
    {
        var clamp = new CutPlaneClamp(4, Below: false);

        await Assert.That(clamp.Apply(new XYZ(1, 2, 0)).IsAlmostEqualTo(new XYZ(1, 2, 4))).IsTrue();
        await Assert.That(CutPlaneClamp.None.Apply(new XYZ(1, 2, 1e6)).Z).IsEqualTo(1e6);
    }
}
