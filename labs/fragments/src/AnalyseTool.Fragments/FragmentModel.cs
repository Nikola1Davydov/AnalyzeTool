namespace AnalyseTool.Fragments
{
    /// <summary>
    /// The input to <see cref="FragmentWriter"/>: a whole model, expressed in the terms the .frag
    /// format actually uses, but with none of its index bookkeeping. Callers describe items,
    /// geometry and materials; the writer derives every flat array and id from this.
    /// <para>
    /// Deliberately free of any Revit type — the Revit mapper builds one of these, and this library
    /// stays testable without Revit. Coordinates are METRES (Revit's internal feet must be converted
    /// by the caller) in a Z-up right-handed frame, which is what both Revit and .frag use.
    /// </para>
    /// </summary>
    public sealed class FragmentModel
    {
        /// <summary>Model-level unique id. Written verbatim; a GUID string is what their importer emits.</summary>
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>Free-form JSON blob, e.g. <c>{"schema":"Revit 2025","source":"AnalyseTool"}</c>.</summary>
        public string? MetadataJson { get; set; }

        /// <summary>
        /// Where the model sits in the world. Geometry stays local to each item so that float32
        /// point coordinates keep their precision; this carries the large survey offset instead.
        /// </summary>
        public FragTransform Coordinates { get; set; } = FragTransform.Identity;

        /// <summary>Unique materials. <see cref="FragSample.MaterialIndex"/> indexes into this.</summary>
        public List<FragMaterial> Materials { get; } = new();

        /// <summary>Unique geometries. <see cref="FragSample.ShellIndex"/> indexes into this.</summary>
        public List<FragShell> Shells { get; } = new();

        /// <summary>
        /// Every item in the model, with or without geometry. Order defines the localId table, and
        /// the writer keeps it — an item's position here is its index for the whole file.
        /// </summary>
        public List<FragItem> Items { get; } = new();
    }

    /// <summary>One element. In Revit terms: one <c>Element</c>, keyed by its ElementId.</summary>
    public sealed class FragItem
    {
        public FragItem(uint localId, string category)
        {
            LocalId = localId;
            Category = category;
        }

        /// <summary>
        /// File-specific identity. Feeding Revit's ElementId straight in is the whole point of writing
        /// our own exporter: it keeps the viewer's selection addressable by the same id the
        /// CommandQueue already speaks.
        /// </summary>
        public uint LocalId { get; }

        /// <summary>Revit category name, e.g. <c>Walls</c>. Stored per item, not pooled.</summary>
        public string Category { get; }

        /// <summary>Optional stable guid — Revit's <c>UniqueId</c> is the natural fit.</summary>
        public string? Guid { get; set; }

        public List<FragAttribute> Attributes { get; } = new();

        /// <summary>
        /// Where this item's geometry sits. Required once <see cref="Samples"/> is non-empty; items
        /// without geometry ignore it.
        /// </summary>
        public FragTransform Placement { get; set; } = FragTransform.Identity;

        /// <summary>
        /// The item's geometry instances. Several samples sharing one <see cref="FragSample.ShellIndex"/>
        /// across many items is exactly the instancing an IFC round-trip tends to flatten away.
        /// </summary>
        public List<FragSample> Samples { get; } = new();
    }

    /// <summary>A single name/value/type triple, serialized as the JSON array the format stores.</summary>
    public sealed record FragAttribute(string Name, string Value, string Type = "String");

    /// <summary>One placed instance of a geometry, with its material and an optional local offset.</summary>
    public sealed class FragSample
    {
        public FragSample(int shellIndex, int materialIndex)
        {
            ShellIndex = shellIndex;
            MaterialIndex = materialIndex;
        }

        public int ShellIndex { get; }

        public int MaterialIndex { get; }

        /// <summary>Applied before the owning item's <see cref="FragItem.Placement"/>.</summary>
        public FragTransform LocalTransform { get; set; } = FragTransform.Identity;
    }

    /// <summary>
    /// A geometry as the format stores it: shared points plus polygonal faces that index into them.
    /// Profiles must be PLANAR — the viewer triangulates them with earcut, which works in 2D.
    /// Emitting Revit's tessellated triangles as 3-point profiles is always safe.
    /// </summary>
    public sealed class FragShell
    {
        public List<Vec3> Points { get; } = new();

        public List<FragProfile> Profiles { get; } = new();

        public List<FragHole> Holes { get; } = new();
    }

    /// <summary>
    /// One face loop. <paramref name="FaceId"/> groups several profiles as one original face, which is
    /// what makes face-level picking work; Revit's face index within a solid maps onto it directly.
    /// </summary>
    public sealed record FragProfile(IReadOnlyList<int> Indices, ushort FaceId = 0);

    /// <summary>An inner loop cut out of the profile at <paramref name="ProfileId"/>.</summary>
    public sealed record FragHole(IReadOnlyList<int> Indices, ushort ProfileId = 0);

    /// <summary>RGBA plus whether back faces are drawn.</summary>
    public sealed record FragMaterial(byte R, byte G, byte B, byte A = 255, bool DoubleSided = false)
    {
        public static FragMaterial Grey { get; } = new(154, 166, 178);
    }

    public readonly record struct Vec3(double X, double Y, double Z);

    /// <summary>
    /// A placement. The Z axis is implied by X × Y, so the two directions must stay orthonormal and
    /// right-handed — Revit's <c>Transform.BasisX</c>/<c>BasisY</c> already are.
    /// </summary>
    public readonly record struct FragTransform(Vec3 Position, Vec3 XDirection, Vec3 YDirection)
    {
        public static FragTransform Identity { get; } =
            new(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

        public static FragTransform At(double x, double y, double z) =>
            Identity with { Position = new Vec3(x, y, z) };
    }
}
