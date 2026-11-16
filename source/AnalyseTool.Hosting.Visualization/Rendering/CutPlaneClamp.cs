namespace AnalyseTool.Hosting.Visualization.Rendering;

/// <summary>
///     How a view moves the geometry before drawing it: a plan presses everything on the viewer's side of its cut plane
///     onto that plane.
/// </summary>
/// <remarks>
///     A plan draws the overlay from its cut plane to the end of its view range, but the cut of an element covers what lies
///     behind the plane — inside a wall the overlay would not show — and nothing beyond the view range is drawn. Pressed
///     just in front of the cut plane, the geometry is drawn over the cut: a footprint. A floor plan looks down, so what is
///     above the plane comes down to it; a ceiling plan looks up, so what is below goes up to it.
/// </remarks>
/// <param name="Elevation">The elevation the geometry is pressed to, in model coordinates, or infinity for no clamp.</param>
/// <param name="Below">Whether the geometry above comes down — a floor plan — or the geometry below goes up — a ceiling plan.</param>
internal readonly record struct CutPlaneClamp(double Elevation, bool Below)
{
    /// <summary>How far in front of the cut plane the pressed geometry lies, in feet: about 6 mm, so the cut does not cover it.</summary>
    public const double Gap = 0.02;

    /// <summary>No clamp: 3D views, sections, elevations.</summary>
    public static readonly CutPlaneClamp None = new(double.PositiveInfinity, Below: true);

    /// <summary>The clamp of a view: of its cut plane for floor, ceiling and area plans, <see cref="None" /> otherwise.</summary>
    public static CutPlaneClamp Of(View view)
    {
        if (view is not ViewPlan plan
            || plan.GetViewRange() is not { } range
            || plan.Document.GetElement(range.GetLevelId(PlanViewPlane.CutPlane)) is not Level level)
        {
            return None;
        }

        var below = plan.ViewType != ViewType.CeilingPlan;
        var elevation = level.ProjectElevation + range.GetOffset(PlanViewPlane.CutPlane);
        return new CutPlaneClamp(below ? elevation + Gap : elevation - Gap, below);
    }

    public XYZ Apply(XYZ point)
    {
        if (Below ? point.Z <= Elevation : point.Z >= Elevation)
        {
            return point;
        }

        return new XYZ(point.X, point.Y, Elevation);
    }
}
