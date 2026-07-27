using Autodesk.Revit.DB;

namespace AnalyseTool.Fragments.Revit
{
    /// <summary>
    /// Turns a Revit <see cref="Document"/> into a <see cref="FragmentModel"/> ready for
    /// <see cref="FragmentWriter"/>.
    /// <para>
    /// Two identities are written for every element, and both matter:
    /// <list type="bullet">
    /// <item><c>localId</c> = the ElementId, so a click in the viewer can be turned straight back
    /// into a command against the live model.</item>
    /// <item><c>guid</c> = the IFC guid Revit's own IFC exporter would assign, so the same element
    /// can be matched against a .frag produced from an IFC file.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Geometry that comes from a family symbol is tessellated ONCE and reused by every instance
    /// through its own transform. That is the main reason to write .frag from Revit directly rather
    /// than via IFC, which usually flattens instancing into a mesh per occurrence.
    /// </para>
    /// <para>
    /// Must be called on Revit's API thread — inside <c>IRevitContext.RunInRevitAsync</c>. Read-only:
    /// it opens no transaction and modifies nothing.
    /// </para>
    /// </summary>
    public sealed class RevitFragmentExporter
    {
        /// <summary>Revit works in decimal feet internally; the format stores metres.</summary>
        private const double FeetToMetres = 0.3048;

        private static readonly int[] FallbackColour = { 154, 166, 178 };

        private readonly RevitExportOptions _options;
        private readonly FragmentModel _model = new();

        /// <summary>Set for the duration of an export; materials are resolved against it.</summary>
        private Document? _document;

        /// <summary>Material table, keyed by Revit material id (-1 = the fallback grey).</summary>
        private readonly Dictionary<long, int> _materials = new();

        /// <summary>
        /// Symbol geometry already tessellated, keyed by the symbol's element id. The value is every
        /// shell that symbol produced, one per material it uses.
        /// </summary>
        private readonly Dictionary<long, List<SymbolPart>> _symbols = new();

        public RevitFragmentExporter(RevitExportOptions? options = null)
        {
            _options = options ?? new RevitExportOptions();
        }

        public FragmentModel Export(Document document)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));

            _document = document;
            _model.Guid = Guid.NewGuid().ToString();
            _model.MetadataJson =
                $"{{\"source\":\"AnalyseTool\",\"revit\":\"{Escape(document.Application.VersionNumber)}\"," +
                $"\"document\":\"{Escape(document.Title)}\"}}";

            Options geometryOptions = new()
            {
                DetailLevel = _options.DetailLevel,
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
            };

            FilteredElementCollector collector = _options.ViewId is { } viewId
                ? new FilteredElementCollector(document, viewId)
                : new FilteredElementCollector(document);

            foreach (Element element in collector.WhereElementIsNotElementType())
            {
                // One unreadable element must never abort a whole model export.
                try
                {
                    AddElement(document, element, geometryOptions);
                }
                catch
                {
                    /* skip this element */
                }
            }

            // Built in Revit's Z-up frame; the format stores Y-up (SPEC.md §6).
            Axes.ZUpToYUp(_model);
            return _model;
        }

        private void AddElement(Document document, Element element, Options geometryOptions)
        {
            long id = element.Id.Value;
            if (id <= 0 || id > uint.MaxValue) return; // localIds are uint

            GeometryElement? geometry = element.get_Geometry(geometryOptions);
            if (geometry is null) return;

            FragItem item = new((uint)id, element.Category?.Name ?? "Unknown")
            {
                Guid = ExportGuid(document, element),
            };
            AddAttributes(element, item);

            // A family instance whose geometry is a single GeometryInstance is the case worth
            // optimising: tessellate the symbol once, then place it.
            GeometryInstance? single = SingleInstance(geometry);
            if (single is not null)
            {
                foreach (SymbolPart part in SymbolParts(single, element.GetTypeId().Value))
                    item.Samples.Add(new FragSample(part.ShellIndex, part.MaterialIndex));

                item.Placement = ToTransform(single.Transform);
            }
            else
            {
                // Walls, floors, anything modelled in place: tessellate here. Points are stored
                // relative to the element's own origin so float32 keeps its precision far from 0,0.
                XYZ origin = Origin(element);
                foreach (SymbolPart part in Tessellate(geometry, Transform.Identity, origin))
                    item.Samples.Add(new FragSample(part.ShellIndex, part.MaterialIndex));

                item.Placement = FragTransform.At(
                    origin.X * FeetToMetres, origin.Y * FeetToMetres, origin.Z * FeetToMetres);
            }

            if (item.Samples.Count == 0 && !_options.IncludeItemsWithoutGeometry) return;

            _model.Items.Add(item);
        }

        /// <summary>
        /// The id Revit's IFC exporter would give this element, in IFC's 22-character form. This is
        /// the join key between our .frag and one produced from an IFC file.
        /// </summary>
        private static string ExportGuid(Document document, Element element)
        {
            try
            {
                return IfcGuid.From(ExportUtils.GetExportId(document, element.Id));
            }
            catch
            {
                // Some elements have no export id; the UniqueId's guid part still beats nothing.
                return IfcGuid.TryGuidFromUniqueId(element.UniqueId, out Guid guid)
                    ? IfcGuid.From(guid)
                    : string.Empty;
            }
        }

        private void AddAttributes(Element element, FragItem item)
        {
            item.Attributes.Add(new FragAttribute("Name", element.Name));
            if (element.Category is { } category)
                item.Attributes.Add(new FragAttribute("Category", category.Name));

            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId &&
                element.Document.GetElement(typeId) is { } type)
            {
                item.Attributes.Add(new FragAttribute("Type", type.Name));
                item.Attributes.Add(new FragAttribute("TypeId", typeId.Value.ToString()));
            }

            if (!_options.IncludeAllParameters) return;

            foreach (Parameter parameter in element.Parameters)
            {
                try
                {
                    if (!parameter.HasValue) continue;
                    string? value = parameter.AsValueString() ?? parameter.AsString();
                    if (string.IsNullOrEmpty(value)) continue;
                    item.Attributes.Add(new FragAttribute(
                        parameter.Definition.Name, value!, parameter.StorageType.ToString()));
                }
                catch
                {
                    /* a single unreadable parameter is not worth losing the element over */
                }
            }
        }

        private static GeometryInstance? SingleInstance(GeometryElement geometry)
        {
            GeometryInstance? found = null;
            foreach (GeometryObject obj in geometry)
            {
                switch (obj)
                {
                    case GeometryInstance instance when found is null:
                        found = instance;
                        break;
                    case GeometryInstance:
                        return null; // more than one: fall back to direct tessellation
                    case Solid solid when solid.Faces.Size > 0 && solid.Volume > 0:
                        return null; // mixed content
                }
            }

            return found;
        }

        /// <summary>
        /// Tessellates a family symbol once and caches it, so every instance reuses the same shells.
        /// Keyed by the element's type id — for a family instance that IS the FamilySymbol, and it is
        /// reachable without depending on GeometryInstance exposing its symbol.
        /// </summary>
        private List<SymbolPart> SymbolParts(GeometryInstance instance, long symbolId)
        {
            if (symbolId > 0 && _symbols.TryGetValue(symbolId, out List<SymbolPart>? cached))
                return cached;

            // Symbol geometry is expressed in the symbol's own frame, which is exactly what we want
            // to store: the instance transform then becomes the item's placement.
            List<SymbolPart> parts = Tessellate(instance.GetSymbolGeometry(), Transform.Identity, XYZ.Zero);

            if (symbolId > 0) _symbols[symbolId] = parts;
            return parts;
        }

        /// <summary>
        /// Walks solids, tessellates their faces and groups the triangles into one shell per
        /// material. <paramref name="origin"/> is subtracted from every point.
        /// </summary>
        private List<SymbolPart> Tessellate(
            IEnumerable<GeometryObject> geometry, Transform transform, XYZ origin)
        {
            Dictionary<long, ShellBuilder> byMaterial = new();
            Collect(geometry, transform, origin, byMaterial);

            List<SymbolPart> parts = new();
            foreach (KeyValuePair<long, ShellBuilder> entry in byMaterial)
            {
                if (entry.Value.Shell.Profiles.Count == 0) continue;

                _model.Shells.Add(entry.Value.Shell);
                parts.Add(new SymbolPart(_model.Shells.Count - 1, entry.Value.MaterialIndex));
            }

            return parts;
        }

        private void Collect(
            IEnumerable<GeometryObject> geometry,
            Transform transform,
            XYZ origin,
            Dictionary<long, ShellBuilder> byMaterial)
        {
            foreach (GeometryObject obj in geometry)
            {
                switch (obj)
                {
                    // Nested instance: compose the transform, or every nested family stacks at the
                    // parent's origin.
                    case GeometryInstance nested:
                        Collect(nested.GetSymbolGeometry(), transform.Multiply(nested.Transform),
                            origin, byMaterial);
                        break;

                    case Solid solid when solid.Faces.Size > 0 && solid.Volume > 0:
                        int faceIndex = 0;
                        foreach (Face face in solid.Faces)
                        {
                            AddFace(face, (ushort)(faceIndex & ushort.MaxValue), transform, origin, byMaterial);
                            faceIndex++;
                        }

                        break;
                }
            }
        }

        private void AddFace(
            Face face,
            ushort faceId,
            Transform transform,
            XYZ origin,
            Dictionary<long, ShellBuilder> byMaterial)
        {
            Mesh? mesh = face.Triangulate(_options.TessellationQuality);
            if (mesh is null || mesh.NumTriangles == 0) return;

            long materialId = -1;
            try { materialId = face.MaterialElementId?.Value ?? -1; }
            catch { materialId = -1; }

            if (!byMaterial.TryGetValue(materialId, out ShellBuilder? builder))
            {
                builder = new ShellBuilder(MaterialIndex(materialId));
                byMaterial[materialId] = builder;
            }

            FragShell shell = builder.Shell;
            int baseIndex = shell.Points.Count;

            foreach (XYZ vertex in mesh.Vertices)
            {
                XYZ p = transform.OfPoint(vertex) - origin;
                shell.Points.Add(new Vec3(
                    p.X * FeetToMetres, p.Y * FeetToMetres, p.Z * FeetToMetres));
            }

            // A triangle is trivially planar, so it is always a valid profile — the viewer's earcut
            // pass cannot go wrong on it. Merging coplanar triangles into bigger polygons would
            // shrink the file and is the obvious next optimisation.
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle triangle = mesh.get_Triangle(i);
                shell.Profiles.Add(new FragProfile(
                    new[]
                    {
                        baseIndex + (int)triangle.get_Index(0),
                        baseIndex + (int)triangle.get_Index(1),
                        baseIndex + (int)triangle.get_Index(2),
                    },
                    faceId));
            }
        }

        private int MaterialIndex(long materialId)
        {
            if (_materials.TryGetValue(materialId, out int existing)) return existing;

            int[] colour = FallbackColour;
            byte alpha = 255;
            bool doubleSided = false;

            try
            {
                if (materialId != -1 &&
                    _document?.GetElement(new ElementId(materialId)) is Material material)
                {
                    Color c = material.Color;
                    if (c is not null && c.IsValid)
                        colour = new[] { (int)c.Red, (int)c.Green, (int)c.Blue };

                    // Revit transparency is 0 (opaque) .. 100 (clear).
                    alpha = (byte)Math.Clamp(255 - material.Transparency * 255 / 100, 25, 255);
                    doubleSided = alpha < 255;
                }
            }
            catch
            {
                /* unreadable material → fallback grey */
            }

            _model.Materials.Add(new FragMaterial(
                (byte)colour[0], (byte)colour[1], (byte)colour[2], alpha, doubleSided));

            int index = _model.Materials.Count - 1;
            _materials[materialId] = index;
            return index;
        }

        private static XYZ Origin(Element element)
        {
            try
            {
                BoundingBoxXYZ? box = element.get_BoundingBox(null);
                if (box is not null) return (box.Min + box.Max) / 2.0;
            }
            catch
            {
                /* fall through */
            }

            return XYZ.Zero;
        }

        private static FragTransform ToTransform(Transform t) => new(
            new Vec3(t.Origin.X * FeetToMetres, t.Origin.Y * FeetToMetres, t.Origin.Z * FeetToMetres),
            new Vec3(t.BasisX.X, t.BasisX.Y, t.BasisX.Z),
            new Vec3(t.BasisY.X, t.BasisY.Y, t.BasisY.Z));

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private readonly record struct SymbolPart(int ShellIndex, int MaterialIndex);

        private sealed class ShellBuilder(int materialIndex)
        {
            public FragShell Shell { get; } = new();

            public int MaterialIndex { get; } = materialIndex;
        }
    }

    public sealed class RevitExportOptions
    {
        /// <summary>Export only what a given view shows. Null exports the whole model.</summary>
        public ElementId? ViewId { get; set; }

        public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;

        /// <summary>0 = coarsest, 1 = finest. Drives <see cref="Face.Triangulate(double)"/>.</summary>
        public double TessellationQuality { get; set; } = 0.5;

        /// <summary>Write every readable parameter as an attribute. Large, but that is the point.</summary>
        public bool IncludeAllParameters { get; set; } = true;

        /// <summary>Keep levels, grids and the like — they carry data even with no geometry.</summary>
        public bool IncludeItemsWithoutGeometry { get; set; }
    }
}
