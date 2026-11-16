namespace AnalyseTool.Hosting.Visualization;

/// <summary>
///     What <see cref="IGeometryOverlay" /> draws: Revit geometry turned into triangles and lines, in one colour.
/// </summary>
/// <remarks>
///     <see cref="Add(GeometryObject)" /> reads the geometry at once — on the Revit thread — and keeps only points, so it
///     stays valid when the element changes or is deleted. Faces are drawn with the transparency, edges and curves opaque.
///     Coordinates are in the internal units of Revit (feet), in the model coordinates of the document.
/// </remarks>
[PublicAPI]
public sealed class OverlayGeometry
{
    /// <summary>The size of the cross that marks a point when no size is given: half a foot.</summary>
    public const double DefaultPointSize = 0.5;

    /// <summary>How far the geometry of an element moves out of its surfaces by default: about 6 mm.</summary>
    public const double ElementOffset = 0.02;

    private readonly List<XYZ> _triangles = [];
    private readonly List<XYZ> _lines = [];
    private double _minX = double.MaxValue, _minY = double.MaxValue, _minZ = double.MaxValue;
    private double _maxX = double.MinValue, _maxY = double.MinValue, _maxZ = double.MinValue;

    /// <summary>Creates empty geometry of one colour.</summary>
    /// <param name="color">The colour of faces, edges and curves.</param>
    /// <param name="transparency">The transparency of faces, 0 (opaque) to 100.</param>
    public OverlayGeometry(Color color, int transparency = 50)
    {
        Color = color;
        Transparency = Math.Clamp(transparency, 0, 100);
    }

    /// <summary>Gets the colour of faces, edges and curves.</summary>
    public Color Color { get; }

    /// <summary>Gets the transparency of faces, 0 (opaque) to 100.</summary>
    public int Transparency { get; }

    /// <summary>Gets whether nothing was added that can be drawn.</summary>
    public bool IsEmpty => _triangles.Count == 0 && _lines.Count == 0;

    /// <summary>Gets the corners of the triangles, three per triangle.</summary>
    internal IReadOnlyList<XYZ> Triangles => _triangles;

    /// <summary>Gets the ends of the line segments, two per segment.</summary>
    internal IReadOnlyList<XYZ> Lines => _lines;

    /// <summary>Gets the box around everything added; <see langword="null" /> while empty.</summary>
    internal Outline? Bounds => IsEmpty ? null : new Outline(new XYZ(_minX, _minY, _minZ), new XYZ(_maxX, _maxY, _maxZ));

    /// <summary>
    ///     Adds the geometry of an element as Revit returns it with fine detail: solids with their edges, meshes, curves.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="offset">
    ///     How far faces and edges of solids move out along their normals, in feet: geometry drawn exactly on the element
    ///     would be hidden by the element itself.
    /// </param>
    /// <returns><see langword="false" /> when the element has no geometry, as a view or a type.</returns>
    public bool Add(Element element, double offset = ElementOffset)
    {
        var count = _triangles.Count + _lines.Count;
        if (element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }) is { } geometry)
        {
            Add(geometry, offset);
        }

        return _triangles.Count + _lines.Count > count;
    }

    /// <summary>
    ///     Adds geometry: <see cref="Solid" /> (faces and edges), <see cref="Face" />, <see cref="Mesh" />,
    ///     <see cref="Curve" />, <see cref="Edge" />, <see cref="PolyLine" />, <see cref="Point" />, and the contents of a
    ///     <see cref="GeometryElement" /> or <see cref="GeometryInstance" />. Other kinds are skipped.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="offset">
    ///     How far faces and edges of solids and faces move out along their normals, in feet — for geometry that lies on
    ///     an element; meshes, curves and points stay where they are.
    /// </param>
    /// <returns>This geometry, for chaining.</returns>
    public OverlayGeometry Add(GeometryObject geometry, double offset = 0)
    {
        switch (geometry)
        {
            case GeometryElement element:
                foreach (var child in element)
                {
                    Add(child, offset);
                }

                break;
            case GeometryInstance instance:
                Add(instance.GetInstanceGeometry(), offset);
                break;
            case Solid solid:
                foreach (Face face in solid.Faces)
                {
                    AddFace(face, offset);
                }

                foreach (Edge edge in solid.Edges)
                {
                    AddEdge(edge, offset);
                }

                break;
            case Face face:
                AddFace(face, offset);
                foreach (EdgeArray loop in face.EdgeLoops)
                {
                    foreach (Edge edge in loop)
                    {
                        AddEdge(edge, offset);
                    }
                }

                break;
            case Mesh mesh:
                AddTriangles(mesh);
                break;
            case Curve curve:
                AddPolyline(curve.Tessellate());
                break;
            case Edge edge:
                AddPolyline(edge.Tessellate());
                break;
            case PolyLine polyLine:
                AddPolyline(polyLine.GetCoordinates());
                break;
            case Point point:
                Add(point.Coord);
                break;
        }

        return this;
    }

    /// <summary>Adds a box: its faces and its twelve edges, with the transform of the box applied.</summary>
    /// <returns>This geometry, for chaining.</returns>
    public OverlayGeometry Add(BoundingBoxXYZ box)
    {
        var transform = box.Transform ?? Transform.Identity;
        var (min, max) = (box.Min, box.Max);
        XYZ[] corners =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        ];

        for (var index = 0; index < corners.Length; index++)
        {
            corners[index] = transform.OfPoint(corners[index]);
        }

        // Counterclockwise seen from outside: DirectContext3D shows a triangle from the side its corners turn left
        int[] faces = [0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
        foreach (var index in faces)
        {
            AddVertex(_triangles, corners[index]);
        }

        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        foreach (var index in edges)
        {
            AddVertex(_lines, corners[index]);
        }

        return this;
    }

    /// <summary>Adds a point, drawn as a cross of three lines along the axes.</summary>
    /// <param name="point">The point.</param>
    /// <param name="size">The length of each line of the cross, in feet.</param>
    /// <returns>This geometry, for chaining.</returns>
    public OverlayGeometry Add(XYZ point, double size = DefaultPointSize)
    {
        var half = size / 2;
        foreach (var axis in new[] { XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ })
        {
            AddVertex(_lines, point - axis * half);
            AddVertex(_lines, point + axis * half);
        }

        return this;
    }

    /// <summary>Adds a line segment between two points.</summary>
    /// <returns>This geometry, for chaining.</returns>
    public OverlayGeometry Add(XYZ start, XYZ end)
    {
        AddVertex(_lines, start);
        AddVertex(_lines, end);
        return this;
    }

    private void AddFace(Face face, double offset)
    {
        var mesh = face.Triangulate();
        if (offset == 0)
        {
            AddTriangles(mesh);
            return;
        }

        for (var index = 0; index < mesh.NumTriangles; index++)
        {
            var triangle = mesh.get_Triangle(index);
            for (var corner = 0; corner < 3; corner++)
            {
                var point = triangle.get_Vertex(corner);
                AddVertex(_triangles, point + Normal(face, point) * offset);
            }
        }
    }

    // An edge between two faces moves along both normals, so it stays on the moved faces
    private void AddEdge(Edge edge, double offset)
    {
        var points = edge.Tessellate();
        if (offset != 0)
        {
            var first = edge.GetFace(0);
            var second = edge.GetFace(1);
            points = points.Select(point =>
            {
                var normal = Normal(first, point);
                if (second is not null)
                {
                    normal += Normal(second, point);
                }

                return normal.IsZeroLength() ? point : point + normal.Normalize() * offset;
            }).ToList();
        }

        AddPolyline(points);
    }

    /// <summary>The normal of a face at a point on it, pointing out of its solid.</summary>
    private static XYZ Normal(Face? face, XYZ point)
    {
        return face switch
        {
            PlanarFace planar => planar.FaceNormal,
            not null when face.Project(point) is { } projection => face.ComputeNormal(projection.UVPoint),
            _ => XYZ.Zero
        };
    }

    private void AddTriangles(Mesh mesh)
    {
        for (var index = 0; index < mesh.NumTriangles; index++)
        {
            var triangle = mesh.get_Triangle(index);
            AddVertex(_triangles, triangle.get_Vertex(0));
            AddVertex(_triangles, triangle.get_Vertex(1));
            AddVertex(_triangles, triangle.get_Vertex(2));
        }
    }

    private void AddPolyline(IList<XYZ> points)
    {
        for (var index = 1; index < points.Count; index++)
        {
            AddVertex(_lines, points[index - 1]);
            AddVertex(_lines, points[index]);
        }
    }

    private void AddVertex(List<XYZ> target, XYZ point)
    {
        target.Add(point);
        (_minX, _minY, _minZ) = (Math.Min(_minX, point.X), Math.Min(_minY, point.Y), Math.Min(_minZ, point.Z));
        (_maxX, _maxY, _maxZ) = (Math.Max(_maxX, point.X), Math.Max(_maxY, point.Y), Math.Max(_maxZ, point.Z));
    }
}
