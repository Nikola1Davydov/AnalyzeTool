using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Dwg
{
    /// <summary>
    /// Turns the sidecar's flat geometry into Revit curves, in internal units.
    ///
    /// Two simplifications are deliberate, and both are stated in the command's description because a
    /// user has to know them:
    ///
    /// 1. Everything is FLATTENED to one elevation. A Revit sketch plane is planar, and drafting DWGs
    ///    routinely carry stray Z values on geometry that was drawn as if it were flat; keeping them
    ///    would produce curves that cannot be placed on any single plane.
    /// 2. Only geometry whose extrusion direction is +Z is converted. Anything else lives in its own
    ///    object coordinate system, and placing it as though it did not would put the curve in the wrong
    ///    place silently — the one failure mode worth refusing outright.
    ///
    /// What it will not do is tessellate. A polyline's bulged segment becomes a Revit arc, not 32 short
    /// lines: turning curves into line soup is precisely what makes an exploded DWG unusable, and doing
    /// it here would reproduce the problem this whole feature exists to avoid.
    /// </summary>
    internal sealed class DwgCurveFactory
    {
        private const double NormalTolerance = 1e-6;
        private const double BulgeTolerance = 1e-9;

        private readonly double _feetPerUnit;
        private readonly XYZ _offset;
        private readonly double _elevation;
        private readonly double _shortCurveTolerance;

        /// <param name="feetPerUnit">Revit internal feet per drawing unit (see <see cref="DwgUnits"/>).</param>
        /// <param name="offset">Shift applied after scaling, in feet. Used to bring survey coordinates
        /// back near the origin, where Revit's accuracy warnings stop.</param>
        /// <param name="elevation">World Z, in feet, that every curve is placed at.</param>
        /// <param name="shortCurveTolerance">The document's <c>ShortCurveTolerance</c>. Curves below it
        /// are refused by Revit, and a DWG full of zero-length line stubs is a common sight.</param>
        public DwgCurveFactory(double feetPerUnit, XYZ offset, double elevation, double shortCurveTolerance)
        {
            _feetPerUnit = feetPerUnit;
            _offset = offset;
            _elevation = elevation;
            _shortCurveTolerance = shortCurveTolerance;
        }

        /// <summary>
        /// Appends the curves for one entity to <paramref name="into"/>.
        /// </summary>
        /// <returns>True when at least one curve was produced. False sets <paramref name="reason"/> to a
        /// short phrase naming why nothing was — the caller groups those, so "812 skipped" can say what
        /// the 812 were instead of leaving the user to guess.</returns>
        public bool TryCreate(DwgGeometry geometry, ICollection<Curve> into, out string? reason)
        {
            reason = null;
            int before = into.Count;

            switch (geometry.Kind)
            {
                case "line":
                    if (!Planar(geometry.Normal, out reason)) return false;
                    AddSegment(into, geometry.Start, geometry.End);
                    break;

                case "arc":
                    if (!Planar(geometry.Normal, out reason)) return false;
                    AddArc(into, geometry.Center, geometry.Radius, geometry.StartAngle, geometry.EndAngle);
                    break;

                case "circle":
                    if (!Planar(geometry.Normal, out reason)) return false;
                    // A full circle is not a bounded Revit curve, so it goes in as two half arcs — the
                    // same thing Revit itself does when you draw one.
                    AddArc(into, geometry.Center, geometry.Radius, 0, Math.PI);
                    AddArc(into, geometry.Center, geometry.Radius, Math.PI, 2 * Math.PI);
                    break;

                case "ellipse":
                    if (!Planar(geometry.Normal, out reason)) return false;
                    AddEllipse(into, geometry);
                    break;

                case "polyline":
                    AddPolyline(into, geometry);
                    break;

                // Mapped by the reader but not by this importer. Text needs a height in paper millimetres
                // that only the target view's scale can give, and a block needs a Revit family to become;
                // both are decisions, not conversions, and they belong to a later command rather than to a
                // silent default here.
                case "text":
                case "mText":
                    reason = "text is not converted by this command";
                    return false;
                case "insert":
                    reason = "block references need a family mapping";
                    return false;
                case "point":
                    reason = "points have no curve equivalent";
                    return false;

                default:
                    reason = $"unsupported geometry '{geometry.Kind}'";
                    return false;
            }

            if (into.Count > before) return true;

            reason ??= "degenerate or shorter than Revit's minimum curve length";
            return false;
        }

        /// <summary>+Z only. A null normal is +Z: the reader omits it for entities that have no
        /// extrusion direction at all, such as LINE.</summary>
        private static bool Planar(double[]? normal, out string? reason)
        {
            reason = null;
            if (normal is null || normal.Length < 3) return true;

            bool isWorldZ = Math.Abs(normal[0]) < NormalTolerance
                            && Math.Abs(normal[1]) < NormalTolerance
                            && normal[2] > 1 - NormalTolerance;
            if (isWorldZ) return true;

            reason = "extrusion direction is not +Z (rotated object coordinate system)";
            return false;
        }

        private void AddSegment(ICollection<Curve> into, double[]? start, double[]? end)
        {
            if (!TryPoint(start, out XYZ a) || !TryPoint(end, out XYZ b)) return;
            if (a.DistanceTo(b) < _shortCurveTolerance) return;

            into.Add(Line.CreateBound(a, b));
        }

        private void AddArc(ICollection<Curve> into, double[]? center, double radius, double startAngle, double endAngle)
        {
            if (!TryPoint(center, out XYZ c)) return;

            double scaledRadius = radius * _feetPerUnit;
            if (!IsUsable(scaledRadius) || scaledRadius < _shortCurveTolerance) return;

            // DWG arcs run counter-clockwise from start to end; Revit wants an increasing, non-empty
            // range under a full turn.
            double sweep = endAngle - startAngle;
            while (sweep <= 0) sweep += 2 * Math.PI;
            if (sweep > 2 * Math.PI) sweep = 2 * Math.PI;

            // A sweep that reaches a full turn is a circle written as an arc; it cannot be one bounded
            // curve, so it halves like one.
            if (sweep >= 2 * Math.PI - 1e-9)
            {
                into.Add(Arc.Create(c, scaledRadius, startAngle, startAngle + Math.PI, XYZ.BasisX, XYZ.BasisY));
                into.Add(Arc.Create(c, scaledRadius, startAngle + Math.PI, startAngle + 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                return;
            }

            if (scaledRadius * sweep < _shortCurveTolerance) return;

            into.Add(Arc.Create(c, scaledRadius, startAngle, startAngle + sweep, XYZ.BasisX, XYZ.BasisY));
        }

        private void AddEllipse(ICollection<Curve> into, DwgGeometry geometry)
        {
            if (!TryPoint(geometry.Center, out XYZ center)) return;
            double[]? major = geometry.MajorAxis;
            if (major is null || major.Length < 3) return;

            // The major axis is a VECTOR from the centre, so it scales but is not offset.
            XYZ axis = new(major[0] * _feetPerUnit, major[1] * _feetPerUnit, 0);
            double xRadius = axis.GetLength();
            double yRadius = xRadius * geometry.MinorAxisRatio;
            if (!IsUsable(xRadius) || !IsUsable(yRadius) || yRadius < _shortCurveTolerance) return;

            XYZ xAxis = axis.Normalize();
            XYZ yAxis = XYZ.BasisZ.CrossProduct(xAxis);

            double start = geometry.StartParameter;
            double end = geometry.EndParameter;
            double sweep = end - start;
            while (sweep <= 0) sweep += 2 * Math.PI;

            if (sweep >= 2 * Math.PI - 1e-9)
            {
                AddEllipseArc(into, center, xRadius, yRadius, xAxis, yAxis, start, start + Math.PI);
                AddEllipseArc(into, center, xRadius, yRadius, xAxis, yAxis, start + Math.PI, start + 2 * Math.PI);
                return;
            }

            AddEllipseArc(into, center, xRadius, yRadius, xAxis, yAxis, start, start + sweep);
        }

        private static void AddEllipseArc(
            ICollection<Curve> into, XYZ center, double xRadius, double yRadius, XYZ xAxis, XYZ yAxis, double start, double end)
        {
            Curve curve = Ellipse.CreateCurve(center, xRadius, yRadius, xAxis, yAxis, start, end);
            // CreateCurve returns a bound curve for a partial range; MakeBound is the belt to that
            // braces, and a no-op when it is already bound.
            if (!curve.IsBound) curve.MakeBound(start, end);
            into.Add(curve);
        }

        /// <summary>
        /// A polyline segment at a time. The bulge on a vertex is tan(sweep/4) of the arc running to the
        /// NEXT vertex, so the arc's mid-point sits at chordMidpoint + perpendicular * (bulge * chord / 2)
        /// — which is exactly the three points Revit's arc-through-points overload wants.
        /// </summary>
        private void AddPolyline(ICollection<Curve> into, DwgGeometry geometry)
        {
            IReadOnlyList<DwgVertex>? vertices = geometry.Vertices;
            if (vertices is null || vertices.Count < 2) return;

            int segments = geometry.Closed ? vertices.Count : vertices.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                DwgVertex from = vertices[i];
                DwgVertex to = vertices[(i + 1) % vertices.Count];

                if (!TryPoint(from.Point, out XYZ a) || !TryPoint(to.Point, out XYZ b)) continue;
                if (a.DistanceTo(b) < _shortCurveTolerance) continue;

                if (Math.Abs(from.Bulge) < BulgeTolerance || !IsUsable(from.Bulge))
                {
                    into.Add(Line.CreateBound(a, b));
                    continue;
                }

                XYZ chord = b - a;
                double length = chord.GetLength();
                // Left of the chord for a positive bulge, which is the counter-clockwise side.
                XYZ perpendicular = new(-chord.Y / length, chord.X / length, 0);
                XYZ middle = (a + b) / 2 + perpendicular * (from.Bulge * length / 2);

                into.Add(Arc.Create(a, b, middle));
            }
        }

        /// <summary>Scales a drawing point into internal feet, shifts it, and drops it onto the import
        /// elevation. Rejects anything non-finite: one NaN from a damaged entity would otherwise become
        /// a Revit exception that aborts the whole transaction.</summary>
        private bool TryPoint(double[]? point, out XYZ result)
        {
            result = XYZ.Zero;
            if (point is null || point.Length < 2) return false;
            if (!IsUsable(point[0]) || !IsUsable(point[1])) return false;

            result = new XYZ(point[0] * _feetPerUnit + _offset.X, point[1] * _feetPerUnit + _offset.Y, _elevation);
            return true;
        }

        private static bool IsUsable(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
