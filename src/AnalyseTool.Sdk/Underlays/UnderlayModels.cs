using System.ComponentModel;
using System.Runtime.Serialization;

namespace AnalyseTool.Sdk.Underlays
{
    // Serialization: [DataContract]/[DataMember], not Newtonsoft's [JsonProperty] — the Sdk's public
    // surface stays free of Newtonsoft (a private dependency here), and Newtonsoft honours DataMember names
    // all the same. The names are camelCase because the schema the host publishes for an OutputType is
    // generated with Web defaults (camelCase); the two must agree, or the schema misnames what it describes.
    // EmitDefaultValue = false ONLY on nullable members: Newtonsoft reads it as "omit the default", and the
    // default of a bool or an int would be dropped too.
    //
    // Documentation: every member has an XML comment, for the C# author. [Description] — what the host
    // publishes in the MCP output schema — only where a reader could not guess the meaning from the name:
    // the listing carries every tool's schema on every connect and is capped at 4096 characters per schema
    // (SchemaListing), and a schema over the cap is listed as a free-form object with no descriptions at all.
    // UnderlayMcpTests.Output_schema_survives_the_listing_cap holds the line.

    /// <summary>What <see cref="UnderlayReader.GetUnderlays"/> answers: every CAD import/link and
    /// PDF/raster image in the document (or in one view), each described well enough that a caller
    /// needs no second call to know what it is looking at.</summary>
    [DataContract]
    public sealed record UnderlaysResult
    {
        /// <summary>Unit of every length and coordinate in the answer: always "mm", project internal coordinates.</summary>
        [DataMember(Name = "units")]
        [Description("Always \"mm\". Coordinates are project-internal (Revit internal origin, not shared). mm / 304.8 = Revit feet.")]
        public string Units { get; init; } = UnderlayReader.Units;

        /// <summary>Number of underlays in <see cref="Underlays"/>.</summary>
        [DataMember(Name = "count")]
        public int Count { get; init; }

        /// <summary>The underlays.</summary>
        [DataMember(Name = "underlays")]
        public IReadOnlyList<UnderlayInfo> Underlays { get; init; } = Array.Empty<UnderlayInfo>();

        /// <summary>Set when the request could not be answered (an unknown view id); <see cref="Underlays"/> is then empty.</summary>
        [DataMember(Name = "error", EmitDefaultValue = false)]
        public string? Error { get; init; }
    }

    /// <summary>One CAD instance or image instance.</summary>
    [DataContract]
    public sealed record UnderlayInfo
    {
        /// <summary>Element id of the ImportInstance / ImageInstance.</summary>
        [DataMember(Name = "id")]
        public long Id { get; init; }

        /// <summary>Display name, usually the file name ("A-101.dwg", "Fassade.pdf").</summary>
        [DataMember(Name = "name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>File kind from the extension: "dwg", "dxf", "dgn", "sat", "skp"… for CAD ("cad" when
        /// unknown), "pdf" for a PDF page, "image" for any other raster.</summary>
        [DataMember(Name = "kind")]
        [Description("dwg, dxf, dgn, sat, skp… (\"cad\" if unknown), pdf, or image (other rasters).")]
        public string Kind { get; init; } = string.Empty;

        /// <summary>"link" (references a file, can be reloaded), "import" (embedded in the model) or
        /// "internal" (an image Revit generated).</summary>
        [DataMember(Name = "source")]
        [Description("link, import or internal.")]
        public string Source { get; init; } = string.Empty;

        /// <summary>Set when the underlay lives INSIDE a loaded Revit link: the link instance's id in this
        /// document. The underlay's own id, owner view and level are then ids of the LINKED model — pass this
        /// id along with them to GetCadLayers / GetCadGeometry / GetPdfPageAsImage. Coordinates are already
        /// transformed into this project.</summary>
        [DataMember(Name = "linkInstanceId", EmitDefaultValue = false)]
        [Description("Set if inside a Revit link: pass it with id to the other underlay tools. Ids are then the link's; coordinates are ours.")]
        public long? LinkInstanceId { get; init; }

        /// <summary>Name of that Revit link.</summary>
        [DataMember(Name = "linkName", EmitDefaultValue = false)]
        public string? LinkName { get; init; }

        /// <summary>Path of the source file as Revit stores it; absent for an import that no longer knows it.</summary>
        [DataMember(Name = "filePath", EmitDefaultValue = false)]
        public string? FilePath { get; init; }

        /// <summary>"loaded", "notFound", "unloaded", "locallyUnloaded", "inClosedWorkset", "imported",
        /// "failedToLoad", "generated"… A link that is not loaded shows nothing and has no geometry.</summary>
        [DataMember(Name = "fileStatus", EmitDefaultValue = false)]
        [Description("loaded, imported, notFound, unloaded, failedToLoad… Not loaded = no geometry.")]
        public string? FileStatus { get; init; }

        /// <summary>True when the underlay belongs to ONE view ("current view only" CAD, every PDF/image);
        /// false for a model-wide CAD import visible in every view that shows its level.</summary>
        [DataMember(Name = "viewSpecific")]
        [Description("True: exists only on ownerView. False: model-wide CAD.")]
        public bool ViewSpecific { get; init; }

        /// <summary>The view a view-specific underlay lives on.</summary>
        [DataMember(Name = "ownerViewId", EmitDefaultValue = false)]
        public long? OwnerViewId { get; init; }

        /// <summary>Name of that view.</summary>
        [DataMember(Name = "ownerViewName", EmitDefaultValue = false)]
        public string? OwnerViewName { get; init; }

        /// <summary>Revit ViewType of that view: "FloorPlan", "DrawingSheet", "Elevation"…</summary>
        [DataMember(Name = "ownerViewType", EmitDefaultValue = false)]
        public string? OwnerViewType { get; init; }

        /// <summary>Scale of that view as N in 1:N. Absent for a sheet.</summary>
        [DataMember(Name = "ownerViewScale", EmitDefaultValue = false)]
        public int? OwnerViewScale { get; init; }

        /// <summary>Sheet number when the owner view is a sheet.</summary>
        [DataMember(Name = "sheetNumber", EmitDefaultValue = false)]
        public string? SheetNumber { get; init; }

        /// <summary>Base level of a model-wide CAD import.</summary>
        [DataMember(Name = "levelId", EmitDefaultValue = false)]
        public long? LevelId { get; init; }

        /// <summary>Name of that level.</summary>
        [DataMember(Name = "levelName", EmitDefaultValue = false)]
        public string? LevelName { get; init; }

        /// <summary>Workset name in a workshared model.</summary>
        [DataMember(Name = "workset", EmitDefaultValue = false)]
        public string? Workset { get; init; }

        /// <summary>Whether the element is pinned.</summary>
        [DataMember(Name = "pinned")]
        public bool Pinned { get; init; }

        /// <summary>"background" or "foreground": drawn behind or in front of the model.</summary>
        [DataMember(Name = "drawLayer", EmitDefaultValue = false)]
        public string? DrawLayer { get; init; }

        /// <summary>[x, y, z] in mm. CAD: the insertion point — the file's own origin placed in the
        /// project. Image: its centre.</summary>
        [DataMember(Name = "origin", EmitDefaultValue = false)]
        [Description("[x,y,z]. CAD: where the file's origin sits. Image: its centre.")]
        public double[]? Origin { get; init; }

        /// <summary>CAD: rotation of the file's X axis about project Z, degrees counter-clockwise.</summary>
        [DataMember(Name = "rotationDegrees", EmitDefaultValue = false)]
        public double? RotationDegrees { get; init; }

        /// <summary>[x, y, z] in mm, lower corner of the bounding box (in the owner view for a
        /// view-specific underlay).</summary>
        [DataMember(Name = "bboxMin", EmitDefaultValue = false)]
        public double[]? BboxMin { get; init; }

        /// <summary>[x, y, z] in mm, upper corner of the bounding box.</summary>
        [DataMember(Name = "bboxMax", EmitDefaultValue = false)]
        public double[]? BboxMax { get; init; }

        /// <summary>CAD: the unit the file was imported with, as Revit displays it.</summary>
        [DataMember(Name = "importUnits", EmitDefaultValue = false)]
        public string? ImportUnits { get; init; }

        /// <summary>CAD: number of layers (Revit subcategories of the import).</summary>
        [DataMember(Name = "layerCount", EmitDefaultValue = false)]
        public int? LayerCount { get; init; }

        /// <summary>CAD: number of geometric primitives over all layers.</summary>
        [DataMember(Name = "primitiveCount", EmitDefaultValue = false)]
        public int? PrimitiveCount { get; init; }

        /// <summary>CAD: every layer with its colour and primitive counts by type — enough to tell the grid
        /// layer (lines) from the wall layer (polylines) from the door layer (blocks) without another call.
        /// Absent when layers were not asked for or the file is not loaded.</summary>
        [DataMember(Name = "layers", EmitDefaultValue = false)]
        public IReadOnlyList<CadLayerSummary>? Layers { get; init; }

        /// <summary>PDF / raster only: page, resolution, paper size, placed size and scale.</summary>
        [DataMember(Name = "image", EmitDefaultValue = false)]
        public UnderlayImageInfo? Image { get; init; }

        /// <summary>One sentence a person would say about this underlay — kind, where, how big, what is in it.</summary>
        [DataMember(Name = "summary")]
        public string Summary { get; init; } = string.Empty;
    }

    /// <summary>The image-specific part of an underlay (PDF page or raster).</summary>
    [DataContract]
    public sealed record UnderlayImageInfo
    {
        /// <summary>PDF: the 1-based page of the file that was placed.</summary>
        [DataMember(Name = "pageNumber", EmitDefaultValue = false)]
        public int? PageNumber { get; init; }

        /// <summary>Resolution the image was rasterised or stored at, in dots per inch.</summary>
        [DataMember(Name = "resolutionDpi", EmitDefaultValue = false)]
        public double? ResolutionDpi { get; init; }

        /// <summary>Native (paper) width in mm — pixels ÷ DPI. 594 × 420 is an A2 page.</summary>
        [DataMember(Name = "paperWidthMm", EmitDefaultValue = false)]
        public double? PaperWidthMm { get; init; }

        /// <summary>Native (paper) height in mm.</summary>
        [DataMember(Name = "paperHeightMm", EmitDefaultValue = false)]
        public double? PaperHeightMm { get; init; }

        /// <summary>Width the image occupies where it is placed, in mm (paper mm on a sheet).</summary>
        [DataMember(Name = "placedWidthMm", EmitDefaultValue = false)]
        public double? PlacedWidthMm { get; init; }

        /// <summary>Height the image occupies where it is placed, in mm.</summary>
        [DataMember(Name = "placedHeightMm", EmitDefaultValue = false)]
        public double? PlacedHeightMm { get; init; }

        /// <summary>Placed width ÷ paper width: N means the drawing is placed at 1:N (100 → 1:100; 1 → as
        /// printed, typical on a sheet). Right only if the PDF itself was plotted 1:1.</summary>
        [DataMember(Name = "scale", EmitDefaultValue = false)]
        [Description("N of 1:N = placed ÷ paper width (1 = paper size, typical on a sheet).")]
        public double? Scale { get; init; }

        /// <summary>Whether width and height are locked to the image's aspect ratio.</summary>
        [DataMember(Name = "lockProportions")]
        public bool LockProportions { get; init; }
    }

    /// <summary>A layer as <see cref="UnderlayReader.GetUnderlays"/> summarises it — what it is and how
    /// much is on it. <see cref="CadLayerInfo"/> is the full record GetCadLayers returns.</summary>
    [DataContract]
    public sealed record CadLayerSummary
    {
        /// <summary>Layer name as in the CAD file ("A-GRID", "A-WALL").</summary>
        [DataMember(Name = "name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>Layer colour as "#RRGGBB".</summary>
        [DataMember(Name = "color", EmitDefaultValue = false)]
        public string? Color { get; init; }

        /// <summary>Number of primitives on the layer. 0 means an empty layer.</summary>
        [DataMember(Name = "primitiveCount")]
        public int PrimitiveCount { get; init; }

        /// <summary>Primitive counts by type — see <see cref="CadLayerInfo.Primitives"/>.</summary>
        [DataMember(Name = "primitives")]
        [Description(CadLayerInfo.PrimitiveTypes)]
        public IReadOnlyDictionary<string, int> Primitives { get; init; } = new Dictionary<string, int>();
    }

    /// <summary>One layer of a CAD import (a subcategory of the import's category), in full.</summary>
    [DataContract]
    public sealed record CadLayerInfo
    {
        internal const string PrimitiveTypes =
            "Count by type: line, arc, circle, ellipse, spline, polyline, point, solid, mesh, block. " +
            "CAD text, dimensions and hatches are not in the Revit API.";

        /// <summary>Layer name as in the CAD file ("A-GRID", "A-WALL").</summary>
        [DataMember(Name = "name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>Id of the Revit subcategory that represents the layer (for visibility overrides).</summary>
        [DataMember(Name = "subcategoryId", EmitDefaultValue = false)]
        public long? SubcategoryId { get; init; }

        /// <summary>Layer colour as "#RRGGBB".</summary>
        [DataMember(Name = "color", EmitDefaultValue = false)]
        public string? Color { get; init; }

        /// <summary>Projection line weight (Revit pen number 1-16).</summary>
        [DataMember(Name = "lineWeight", EmitDefaultValue = false)]
        public int? LineWeight { get; init; }

        /// <summary>Projection line pattern name ("Solid", "Dash"…).</summary>
        [DataMember(Name = "linePattern", EmitDefaultValue = false)]
        public string? LinePattern { get; init; }

        /// <summary>Whether the layer is switched off in the view asked about (or the owner view). Absent
        /// when no view applies.</summary>
        [DataMember(Name = "hiddenInView", EmitDefaultValue = false)]
        [Description("Layer switched off in the view (viewId, else the owner view).")]
        public bool? HiddenInView { get; init; }

        /// <summary>Number of primitives on the layer. 0 means an empty layer.</summary>
        [DataMember(Name = "primitiveCount")]
        public int PrimitiveCount { get; init; }

        /// <summary>Primitive counts by type: "line", "arc", "circle", "ellipse", "spline", "polyline",
        /// "point", "solid", "mesh", "block". CAD text, dimensions and hatch patterns are not exposed by the
        /// Revit API and are not counted.</summary>
        [DataMember(Name = "primitives")]
        [Description(PrimitiveTypes)]
        public IReadOnlyDictionary<string, int> Primitives { get; init; } = new Dictionary<string, int>();
    }

    /// <summary>What <see cref="UnderlayReader.GetCadLayers"/> answers.</summary>
    [DataContract]
    public sealed record CadLayersResult
    {
        /// <summary>The ImportInstance asked about.</summary>
        [DataMember(Name = "importId")]
        public long ImportId { get; init; }

        /// <summary>Its name (file name).</summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string? Name { get; init; }

        /// <summary>The view <see cref="CadLayerInfo.HiddenInView"/> refers to: the one asked for, else the
        /// owner view of a view-specific import.</summary>
        [DataMember(Name = "viewId", EmitDefaultValue = false)]
        public long? ViewId { get; init; }

        /// <summary>Number of layers.</summary>
        [DataMember(Name = "count")]
        public int Count { get; init; }

        /// <summary>The layers, sorted by name.</summary>
        [DataMember(Name = "layers")]
        public IReadOnlyList<CadLayerInfo> Layers { get; init; } = Array.Empty<CadLayerInfo>();

        /// <summary>Set when the id is not a CAD import or the file is not loaded.</summary>
        [DataMember(Name = "error", EmitDefaultValue = false)]
        public string? Error { get; init; }
    }

    /// <summary>What <see cref="UnderlayReader.GetCadGeometry"/> answers.</summary>
    [DataContract]
    public sealed record CadGeometryResult
    {
        /// <summary>The ImportInstance asked about.</summary>
        [DataMember(Name = "importId")]
        public long ImportId { get; init; }

        /// <summary>Its name (file name).</summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string? Name { get; init; }

        /// <summary>Unit of every coordinate and length: always "mm", project internal coordinates, the
        /// import's transform (position, rotation, scale) already applied.</summary>
        [DataMember(Name = "units")]
        [Description("Always \"mm\", project-internal coordinates, the import's position/rotation/scale applied. mm / 304.8 = Revit feet.")]
        public string Units { get; init; } = UnderlayReader.Units;

        /// <summary>Primitives that matched the filters, BEFORE the limit.</summary>
        [DataMember(Name = "count")]
        [Description("Matches before 'limit'. Greater than 'returned' = truncated.")]
        public int Count { get; init; }

        /// <summary>Primitives in <see cref="Primitives"/>. Less than <see cref="Count"/> means the answer was
        /// truncated by the limit.</summary>
        [DataMember(Name = "returned")]
        public int Returned { get; init; }

        /// <summary>The primitives, in file order.</summary>
        [DataMember(Name = "primitives")]
        public IReadOnlyList<CadPrimitive> Primitives { get; init; } = Array.Empty<CadPrimitive>();

        /// <summary>Requested layer names that the import does not have.</summary>
        [DataMember(Name = "unknownLayers", EmitDefaultValue = false)]
        public IReadOnlyList<string>? UnknownLayers { get; init; }

        /// <summary>All layer names of the import — given when a requested layer was unknown.</summary>
        [DataMember(Name = "availableLayers", EmitDefaultValue = false)]
        public IReadOnlyList<string>? AvailableLayers { get; init; }

        /// <summary>Set when the id is not a CAD import or the file is not loaded.</summary>
        [DataMember(Name = "error", EmitDefaultValue = false)]
        public string? Error { get; init; }
    }

    /// <summary>One geometric primitive of a CAD import, in project coordinates (mm).</summary>
    [DataContract]
    public sealed record CadPrimitive
    {
        /// <summary>"line", "arc", "circle", "ellipse", "spline", "polyline", "point", "solid", "mesh" or
        /// "block" (a block reference).</summary>
        [DataMember(Name = "type")]
        public string Type { get; init; } = string.Empty;

        /// <summary>Layer the primitive is on.</summary>
        [DataMember(Name = "layer")]
        public string Layer { get; init; } = string.Empty;

        /// <summary>Name of the block the primitive belongs to (for a "block", the block's own name), when
        /// Revit exposes it.</summary>
        [DataMember(Name = "block", EmitDefaultValue = false)]
        public string? Block { get; init; }

        /// <summary>[[x, y, z], …] in mm. line/arc: start and end; polyline: its vertices; spline/ellipse: a
        /// tessellation; point: the point; block: the insertion point.</summary>
        [DataMember(Name = "points", EmitDefaultValue = false)]
        [Description("[[x,y,z]…]. line/arc: start, end. polyline: vertices. spline/ellipse: tessellated. block: insertion point.")]
        public IReadOnlyList<double[]>? Points { get; init; }

        /// <summary>arc/circle/ellipse: centre [x, y, z] in mm.</summary>
        [DataMember(Name = "center", EmitDefaultValue = false)]
        public double[]? Center { get; init; }

        /// <summary>arc/circle: radius in mm.</summary>
        [DataMember(Name = "radius", EmitDefaultValue = false)]
        public double? Radius { get; init; }

        /// <summary>ellipse: radius along its X direction, mm.</summary>
        [DataMember(Name = "radiusX", EmitDefaultValue = false)]
        public double? RadiusX { get; init; }

        /// <summary>ellipse: radius along its Y direction, mm.</summary>
        [DataMember(Name = "radiusY", EmitDefaultValue = false)]
        public double? RadiusY { get; init; }

        /// <summary>Curve length in mm (line, arc, circle, ellipse, spline, polyline).</summary>
        [DataMember(Name = "length", EmitDefaultValue = false)]
        public double? Length { get; init; }

        /// <summary>polyline: whether the last vertex returns to the first (a closed outline).</summary>
        [DataMember(Name = "closed", EmitDefaultValue = false)]
        public bool? Closed { get; init; }

        /// <summary>solid/mesh: lower corner [x, y, z] in mm.</summary>
        [DataMember(Name = "bboxMin", EmitDefaultValue = false)]
        public double[]? BboxMin { get; init; }

        /// <summary>solid/mesh: upper corner [x, y, z] in mm.</summary>
        [DataMember(Name = "bboxMax", EmitDefaultValue = false)]
        public double[]? BboxMax { get; init; }
    }
}
