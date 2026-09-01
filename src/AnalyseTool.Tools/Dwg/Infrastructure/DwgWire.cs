using Newtonsoft.Json;

namespace AnalyseTool.Tools.Dwg
{
    // The C# half of the wire contract the sidecar declares in src/AnalyseTool.Dwg.Sidecar/src/wire.rs.
    // Two hand-written halves rather than a generated one: the shape is small, and a generator would be
    // more machinery than the thing it generates. Rename a field on one side and the tests on the other
    // side fail, which is the point.
    //
    // Every property spells its wire name out. Newtonsoft writes DECLARED names by default while the
    // schema published for OutputType is generated with Web defaults (camelCase); without the attribute
    // the two disagree, and the sidecar (serde, camelCase) would agree with neither.

    /// <summary>One request line sent to the sidecar.</summary>
    internal sealed class DwgRequest
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("op")] public string Op { get; set; } = string.Empty;
        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)] public string? Path { get; set; }
        [JsonProperty("layers", NullValueHandling = NullValueHandling.Ignore)] public IReadOnlyList<string>? Layers { get; set; }
        [JsonProperty("types", NullValueHandling = NullValueHandling.Ignore)] public IReadOnlyList<string>? Types { get; set; }
        [JsonProperty("space", NullValueHandling = NullValueHandling.Ignore)] public string? Space { get; set; }
        [JsonProperty("maxEntities", NullValueHandling = NullValueHandling.Ignore)] public int? MaxEntities { get; set; }
        [JsonProperty("failsafe", NullValueHandling = NullValueHandling.Ignore)] public bool? Failsafe { get; set; }
    }

    /// <summary>The envelope every response arrives in.</summary>
    internal sealed class DwgResponse<T>
    {
        [JsonProperty("id")] public long? Id { get; set; }
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("result")] public T? Result { get; set; }
        [JsonProperty("error")] public DwgWireError? Error { get; set; }
    }

    /// <summary>A failure from the sidecar. <see cref="Code"/> is the machine-readable half — callers
    /// branch on "this is not a DWG" versus "this DWG is broken" without matching English text.</summary>
    internal sealed class DwgWireError
    {
        [JsonProperty("code")] public string Code { get; set; } = string.Empty;
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    }

    /// <summary>The sidecar's identity, read once per process launch to verify the protocol.</summary>
    public sealed class DwgSidecarInfo
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("version")] public string Version { get; set; } = string.Empty;
        [JsonProperty("protocol")] public int Protocol { get; set; }
        /// <summary>The DWG/DXF codec and its version, e.g. "acadrust 0.4.1".</summary>
        [JsonProperty("codec")] public string Codec { get; set; } = string.Empty;
        [JsonProperty("formats")] public IReadOnlyList<string> Formats { get; set; } = Array.Empty<string>();
        [JsonProperty("ops")] public IReadOnlyList<string> Ops { get; set; } = Array.Empty<string>();
    }

    /// <summary>What is in a drawing, without converting any of it.</summary>
    public sealed class DwgStructure
    {
        [JsonProperty("path")] public string Path { get; set; } = string.Empty;
        /// <summary>"dwg" or "dxf" — what was parsed, not what the caller assumed.</summary>
        [JsonProperty("format")] public string Format { get; set; } = string.Empty;
        /// <summary>DXF version code, e.g. "AC1032" (AutoCAD 2018).</summary>
        [JsonProperty("version")] public string Version { get; set; } = string.Empty;
        [JsonProperty("space")] public string Space { get; set; } = string.Empty;
        [JsonProperty("units")] public DwgUnitsInfo Units { get; set; } = new();
        /// <summary>Entities in the selected space. Block definition contents are NOT counted.</summary>
        [JsonProperty("entityCount")] public int EntityCount { get; set; }
        /// <summary>DXF type name to count, e.g. {"LINE": 12043}.</summary>
        [JsonProperty("byType")] public IReadOnlyDictionary<string, int> ByType { get; set; } = new Dictionary<string, int>();
        [JsonProperty("layers")] public IReadOnlyList<DwgLayer> Layers { get; set; } = Array.Empty<DwgLayer>();
        [JsonProperty("blocks")] public IReadOnlyList<DwgBlock> Blocks { get; set; } = Array.Empty<DwgBlock>();
        [JsonProperty("extents")] public DwgExtents? Extents { get; set; }
        [JsonProperty("notifications")] public IReadOnlyList<DwgNotification> Notifications { get; set; } = Array.Empty<DwgNotification>();
        [JsonProperty("notificationCount")] public int NotificationCount { get; set; }
        [JsonProperty("warnings")] public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    }

    /// <summary>The drawing's INSUNITS. <see cref="Code"/> 0 means UNITLESS, which is common and which
    /// nothing can infer — the caller has to ask a human before any length crosses into Revit.</summary>
    public sealed class DwgUnitsInfo
    {
        [JsonProperty("code")] public int Code { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    }

    public sealed class DwgLayer
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("color")] public DwgColor Color { get; set; } = new();
        [JsonProperty("lineType")] public string LineType { get; set; } = string.Empty;
        [JsonProperty("lineWeightMm")] public double? LineWeightMm { get; set; }
        [JsonProperty("off")] public bool Off { get; set; }
        [JsonProperty("frozen")] public bool Frozen { get; set; }
        [JsonProperty("locked")] public bool Locked { get; set; }
        [JsonProperty("plottable")] public bool Plottable { get; set; }
        [JsonProperty("xrefDependent")] public bool XrefDependent { get; set; }
        /// <summary>Entities on this layer. Zero means the layer is defined but carries nothing.</summary>
        [JsonProperty("entityCount")] public int EntityCount { get; set; }
        [JsonProperty("byType")] public IReadOnlyDictionary<string, int> ByType { get; set; } = new Dictionary<string, int>();
    }

    public sealed class DwgBlock
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        /// <summary>Entities in the block DEFINITION.</summary>
        [JsonProperty("entityCount")] public int EntityCount { get; set; }
        /// <summary>Placed references. A definition with 0 inserts is dead weight in the file.</summary>
        [JsonProperty("insertCount")] public int InsertCount { get; set; }
        [JsonProperty("isXref")] public bool IsXref { get; set; }
    }

    public sealed class DwgColor
    {
        /// <summary>ACI index (1-255) when the colour is indexed.</summary>
        [JsonProperty("index")] public int? Index { get; set; }
        /// <summary>"#RRGGBB" when the colour is a true colour.</summary>
        [JsonProperty("rgb")] public string? Rgb { get; set; }
    }

    public sealed class DwgExtents
    {
        [JsonProperty("min")] public double[] Min { get; set; } = Array.Empty<double>();
        [JsonProperty("max")] public double[] Max { get; set; } = Array.Empty<double>();
    }

    public sealed class DwgNotification
    {
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    }

    /// <summary>The entities of a drawing, in DRAWING units and with all angles in radians.</summary>
    public sealed class DwgEntities
    {
        [JsonProperty("space")] public string Space { get; set; } = string.Empty;
        [JsonProperty("units")] public DwgUnitsInfo Units { get; set; } = new();
        /// <summary>Entities matching the filters, before the cap.</summary>
        [JsonProperty("matched")] public int Matched { get; set; }
        [JsonProperty("returned")] public int Returned { get; set; }
        /// <summary>True when the cap cut the result short — this is a prefix, not the answer.</summary>
        [JsonProperty("truncated")] public bool Truncated { get; set; }
        [JsonProperty("entities")] public IReadOnlyList<DwgEntity> Entities { get; set; } = Array.Empty<DwgEntity>();
        /// <summary>Bounding box of the MATCHED entities, in drawing units. What a caller recentres by:
        /// survey drawings sit hundreds of kilometres from the origin, far enough out that Revit starts
        /// warning about accuracy.</summary>
        [JsonProperty("extents")] public DwgExtents? Extents { get; set; }
        /// <summary>DXF type to count of entities the reader has no geometry mapping for (HATCH,
        /// DIMENSION, 3DSOLID…). Named rather than dropped: "nothing came back" and "4812 hatches were
        /// skipped" are different problems.</summary>
        [JsonProperty("skippedByType")] public IReadOnlyDictionary<string, int> SkippedByType { get; set; } = new Dictionary<string, int>();
        [JsonProperty("warnings")] public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    }

    public sealed class DwgEntity
    {
        /// <summary>The file's own stable id for this entity, as hex.</summary>
        [JsonProperty("handle")] public string Handle { get; set; } = string.Empty;
        [JsonProperty("layer")] public string Layer { get; set; } = string.Empty;
        /// <summary>DXF type name, e.g. "LINE".</summary>
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        [JsonProperty("color")] public DwgColor Color { get; set; } = new();
        /// <summary>Empty means ByLayer.</summary>
        [JsonProperty("lineType")] public string LineType { get; set; } = string.Empty;
        [JsonProperty("geometry")] public DwgGeometry Geometry { get; set; } = new();
    }

    /// <summary>
    /// One entity's geometry. Flat rather than a class per kind with a custom converter: the sidecar
    /// tags each shape with <see cref="Kind"/>, the union is nine shapes wide, and a reader that
    /// switches on one string is easier to follow than a polymorphic binder. Which fields are set
    /// depends on <see cref="Kind"/>; everything else stays null.
    /// </summary>
    public sealed class DwgGeometry
    {
        /// <summary>line | point | circle | arc | ellipse | polyline | text | mText | insert.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;

        [JsonProperty("start")] public double[]? Start { get; set; }
        [JsonProperty("end")] public double[]? End { get; set; }
        [JsonProperty("location")] public double[]? Location { get; set; }
        [JsonProperty("center")] public double[]? Center { get; set; }
        [JsonProperty("radius")] public double Radius { get; set; }
        /// <summary>Radians, measured from the arc's x axis.</summary>
        [JsonProperty("startAngle")] public double StartAngle { get; set; }
        [JsonProperty("endAngle")] public double EndAngle { get; set; }
        /// <summary>The entity's extrusion direction. Anything but +Z means the geometry lives in its
        /// own object coordinate system, which the curve factory refuses rather than misplaces.</summary>
        [JsonProperty("normal")] public double[]? Normal { get; set; }

        [JsonProperty("majorAxis")] public double[]? MajorAxis { get; set; }
        [JsonProperty("minorAxisRatio")] public double MinorAxisRatio { get; set; }
        [JsonProperty("startParameter")] public double StartParameter { get; set; }
        [JsonProperty("endParameter")] public double EndParameter { get; set; }

        [JsonProperty("closed")] public bool Closed { get; set; }
        [JsonProperty("vertices")] public IReadOnlyList<DwgVertex>? Vertices { get; set; }

        [JsonProperty("value")] public string? Value { get; set; }
        [JsonProperty("insertion")] public double[]? Insertion { get; set; }
        [JsonProperty("height")] public double Height { get; set; }
        [JsonProperty("rotation")] public double Rotation { get; set; }
        [JsonProperty("widthFactor")] public double WidthFactor { get; set; }
        [JsonProperty("rectangleWidth")] public double RectangleWidth { get; set; }
        [JsonProperty("style")] public string? Style { get; set; }

        [JsonProperty("blockName")] public string? BlockName { get; set; }
        [JsonProperty("scale")] public double[]? Scale { get; set; }
    }

    /// <summary>One polyline vertex. <see cref="Bulge"/> is tan(sweep/4) of the arc running to the NEXT
    /// vertex and 0 for a straight segment — kept instead of pre-tessellated, because an arc turned into
    /// 32 short lines is exactly the mess that makes an exploded DWG unusable in Revit.</summary>
    public sealed class DwgVertex
    {
        [JsonProperty("point")] public double[] Point { get; set; } = Array.Empty<double>();
        [JsonProperty("bulge")] public double Bulge { get; set; }
    }
}
