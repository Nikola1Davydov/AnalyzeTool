using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Underlays
{
    /// <summary>
    /// Reads what is IN the underlays of a document — CAD imports and links (DWG, DXF, DGN, SAT…) and
    /// PDF/raster images — as plain data: file, placement, layers, primitives, page and scale.
    /// <para>Every method is a function of a <see cref="Document"/> and must be called on the Revit
    /// thread, i.e. inside <see cref="IRevitContext.RunInRevitAsync{T}"/>. Nothing is modified.</para>
    /// <para>Part of the Sdk (not of a built-in command) so an extension reuses the reading instead of
    /// re-deriving it: geometry through nested <see cref="GeometryInstance"/>s with their transforms,
    /// layers through <see cref="GraphicsStyle"/>s, file state through external file references, image
    /// size through pixels and DPI. The built-in commands GetUnderlays, GetCadLayers and GetCadGeometry
    /// are thin wrappers around it.</para>
    /// </summary>
    public static class UnderlayReader
    {
        /// <summary>The one unit every length and coordinate is reported in.</summary>
        public const string Units = "mm";

        /// <summary>Default cap of <see cref="GetCadGeometry"/>.</summary>
        public const int DefaultGeometryLimit = 1000;

        /// <summary>Hard cap of <see cref="GetCadGeometry"/>, whatever the caller asks for.</summary>
        public const int MaxGeometryLimit = 20000;

        private const double MmPerFoot = 304.8;
        private const string NoLayer = "(no layer)";

        /// <summary>
        /// Every underlay of the document, or those visible in one view, each with its file, placement,
        /// bounding box and — for CAD — a per-layer summary of what it contains.
        /// </summary>
        /// <param name="doc">The document to read.</param>
        /// <param name="viewId">Only underlays visible in this view (model-wide imports it shows plus the
        /// ones placed on it). Null for the whole document.</param>
        /// <param name="kinds">Only these kinds ("dwg", "pdf", "image"…; also "cad" for every CAD kind).
        /// Null or empty for all.</param>
        /// <param name="includeLayers">Walk CAD geometry for the per-layer summary. The walk is linear in the
        /// size of the file; false skips it for a quick inventory.</param>
        public static UnderlaysResult GetUnderlays(Document doc, long? viewId = null, IReadOnlyCollection<string>? kinds = null, bool includeLayers = true)
        {
            FilteredElementCollector collector;
            if (viewId is { } vid)
            {
                if (doc.GetElement(new ElementId(vid)) is not View view)
                    return new UnderlaysResult { Error = $"No view with id {vid}. GetViewsAndSheets lists the views and sheets." };
                try { collector = new FilteredElementCollector(doc, view.Id); }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Schedules, legends' browser nodes and the like: views that cannot show elements.
                    return new UnderlaysResult { Error = $"View {vid} ('{view.Name}', {view.ViewType}) cannot show underlays." };
                }
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }

            HashSet<string>? wanted = kinds is { Count: > 0 }
                ? new HashSet<string>(kinds.Select(k => k.Trim().ToLowerInvariant()))
                : null;

            List<UnderlayInfo> underlays = new();
            foreach (Element element in collector
                         .WherePasses(new ElementMulticlassFilter(new[] { typeof(ImportInstance), typeof(ImageInstance) }))
                         .ToElements())
            {
                // The kind is decided BEFORE describing: describing a CAD file walks its geometry, and a
                // request for PDFs has no business paying for every DWG in the model.
                string? kind = element switch
                {
                    ImportInstance cad => CadKind(ExternalFile(doc, doc.GetElement(cad.GetTypeId())).Path ?? CadName(doc, cad)),
                    ImageInstance image => ImageKind(doc, image),
                    _ => null,
                };
                if (kind is null) continue;
                if (wanted is not null && !wanted.Contains(kind) && !(wanted.Contains("cad") && IsCadKind(kind)))
                    continue;

                underlays.Add(element is ImportInstance import
                    ? DescribeCad(doc, import, includeLayers)
                    : DescribeImage(doc, (ImageInstance)element));
            }

            return new UnderlaysResult { Count = underlays.Count, Underlays = underlays };
        }

        /// <summary>The layers of one CAD import with colour, line weight and pattern, visibility in a view
        /// and primitive counts.</summary>
        /// <param name="doc">The document to read.</param>
        /// <param name="importId">Element id of the ImportInstance.</param>
        /// <param name="viewId">The view to report layer visibility for. Null: the owner view of a
        /// view-specific import, else no visibility.</param>
        public static CadLayersResult GetCadLayers(Document doc, long importId, long? viewId = null)
        {
            if (doc.GetElement(new ElementId(importId)) is not ImportInstance cad)
                return new CadLayersResult { ImportId = importId, Error = NotCadMessage(doc, importId) };

            View? view = ResolveView(doc, cad, viewId);
            if (viewId is not null && view is null)
                return new CadLayersResult { ImportId = importId, Name = CadName(doc, cad), Error = $"No view with id {viewId}." };

            List<CadLayerInfo> layers = BuildLayers(doc, cad, view, detailed: true);
            return new CadLayersResult
            {
                ImportId = importId,
                Name = CadName(doc, cad),
                ViewId = view?.Id.Value,
                Count = layers.Count,
                Layers = layers,
                Error = IsLoaded(doc, cad) ? null : NotLoadedMessage(doc, cad),
            };
        }

        /// <summary>
        /// The primitives of one CAD import in project coordinates (mm), with the import's transform —
        /// position, rotation, scale, nested blocks — already applied.
        /// </summary>
        /// <param name="doc">The document to read.</param>
        /// <param name="importId">Element id of the ImportInstance.</param>
        /// <param name="layers">Only primitives on these layers (case-insensitive). Null or empty for all.</param>
        /// <param name="types">Only these primitive types ("line", "polyline", "arc", "block"…). Null or empty for all.</param>
        /// <param name="limit">Cap on primitives returned (default <see cref="DefaultGeometryLimit"/>, at most
        /// <see cref="MaxGeometryLimit"/>). The answer's count still says how many matched.</param>
        public static CadGeometryResult GetCadGeometry(Document doc, long importId, IReadOnlyCollection<string>? layers = null,
                                                       IReadOnlyCollection<string>? types = null, int? limit = null)
        {
            if (doc.GetElement(new ElementId(importId)) is not ImportInstance cad)
                return new CadGeometryResult { ImportId = importId, Error = NotCadMessage(doc, importId) };

            string name = CadName(doc, cad);
            int cap = Math.Clamp(limit ?? DefaultGeometryLimit, 0, MaxGeometryLimit);

            HashSet<string>? layerFilter = layers is { Count: > 0 }
                ? new HashSet<string>(layers, StringComparer.OrdinalIgnoreCase)
                : null;
            HashSet<string>? typeFilter = types is { Count: > 0 }
                ? new HashSet<string>(types.Select(t => t.Trim().ToLowerInvariant()))
                : null;

            List<CadItem> items = CadGeometryWalker.Walk(cad);
            if (items.Count == 0 && !IsLoaded(doc, cad))
                return new CadGeometryResult { ImportId = importId, Name = name, Error = NotLoadedMessage(doc, cad) };

            LayerNames layerNames = new(doc);
            List<string>? unknownLayers = null;
            List<string>? availableLayers = null;
            if (layerFilter is not null)
            {
                HashSet<string> all = new(AllLayerNames(cad, items, layerNames), StringComparer.OrdinalIgnoreCase);
                unknownLayers = layerFilter.Where(l => !all.Contains(l)).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
                if (unknownLayers.Count > 0) availableLayers = all.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
                else unknownLayers = null;
            }

            int count = 0;
            List<CadPrimitive> primitives = new();
            foreach (CadItem item in items)
            {
                string? type = CadGeometryWalker.Classify(item.Object);
                if (type is null) continue;
                if (typeFilter is not null && !typeFilter.Contains(type)) continue;
                string layer = layerNames.Of(item.StyleId);
                if (layerFilter is not null && !layerFilter.Contains(layer)) continue;

                count++;
                if (primitives.Count >= cap) continue;
                // One curve Revit cannot transform (a degenerate spline, a non-conformal block scale) costs
                // that primitive, not the answer.
                CadPrimitive? primitive;
                try { primitive = ToPrimitive(item, type, layer); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { primitive = null; }
                if (primitive is not null) primitives.Add(primitive);
            }

            return new CadGeometryResult
            {
                ImportId = importId,
                Name = name,
                Count = count,
                Returned = primitives.Count,
                Primitives = primitives,
                UnknownLayers = unknownLayers,
                AvailableLayers = availableLayers,
            };
        }

        // ── CAD ─────────────────────────────────────────────────────────────────────────────────────

        private static UnderlayInfo DescribeCad(Document doc, ImportInstance cad, bool includeLayers)
        {
            Element? type = doc.GetElement(cad.GetTypeId());
            string name = CadName(doc, cad);
            (string? path, string? status) = ExternalFile(doc, type);
            string kind = CadKind(path ?? name);
            View? owner = cad.ViewSpecific ? doc.GetElement(cad.OwnerViewId) as View : null;

            Transform transform = cad.GetTotalTransform();
            (double[]? min, double[]? max) = Bounds(cad.get_BoundingBox(owner));

            // A level means something only for a model-wide import; a view-specific one belongs to its view.
            Level? level = null;
            if (!cad.ViewSpecific)
            {
                ElementId levelId = Try<ElementId?>(() => cad.get_Parameter(BuiltInParameter.IMPORT_BASE_LEVEL)?.AsElementId()) ?? ElementId.InvalidElementId;
                if (levelId == ElementId.InvalidElementId) levelId = cad.LevelId;
                level = doc.GetElement(levelId) as Level;
            }

            List<CadLayerSummary>? layers = includeLayers
                ? BuildLayers(doc, cad, owner, detailed: false)
                    .Select(l => new CadLayerSummary { Name = l.Name, Color = l.Color, PrimitiveCount = l.PrimitiveCount, Primitives = l.Primitives })
                    .ToList()
                : null;
            int? primitiveCount = layers?.Sum(l => l.PrimitiveCount);

            UnderlayInfo info = new()
            {
                Id = cad.Id.Value,
                Name = name,
                Kind = kind,
                Source = cad.IsLinked ? "link" : "import",
                FilePath = path,
                FileStatus = status ?? (cad.IsLinked ? null : "imported"),
                ViewSpecific = cad.ViewSpecific,
                OwnerViewId = owner?.Id.Value,
                OwnerViewName = owner?.Name,
                OwnerViewType = owner?.ViewType.ToString(),
                OwnerViewScale = ViewScale(owner),
                SheetNumber = (owner as ViewSheet)?.SheetNumber,
                LevelId = level?.Id.Value,
                LevelName = level?.Name,
                Workset = WorksetName(doc, cad),
                Pinned = cad.Pinned,
                DrawLayer = cad.ViewSpecific ? CadDrawLayer(cad) : null,
                Origin = Point(transform.Origin),
                RotationDegrees = Math.Round(Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * 180 / Math.PI, 3),
                BboxMin = min,
                BboxMax = max,
                ImportUnits = Try<string?>(() => type?.get_Parameter(BuiltInParameter.IMPORT_DISPLAY_UNITS)?.AsValueString()),
                LayerCount = layers?.Count,
                PrimitiveCount = primitiveCount,
                Layers = layers,
            };
            return info with { Summary = CadSummary(info) };
        }

        private static string CadSummary(UnderlayInfo u)
        {
            string where = u.OwnerViewName is not null
                ? $"on {(u.SheetNumber is not null ? $"sheet {u.SheetNumber} " : "view ")}'{u.OwnerViewName}' only"
                : u.LevelName is not null ? $"model-wide on level '{u.LevelName}'" : "model-wide";
            string what = u.Layers is null ? string.Empty
                : $", {u.LayerCount} layers, {u.PrimitiveCount} primitives" +
                  (u.Layers.Count > 0
                      ? " (busiest: " + string.Join(", ", u.Layers.Where(l => l.PrimitiveCount > 0)
                                                                    .OrderByDescending(l => l.PrimitiveCount)
                                                                    .Take(3)
                                                                    .Select(l => $"{l.Name} {l.PrimitiveCount}")) + ")"
                      : string.Empty);
            string state = u.FileStatus is null or "loaded" or "imported" ? string.Empty : $", file {u.FileStatus}";
            string extent = u.BboxMin is { Length: 3 } lo && u.BboxMax is { Length: 3 } hi
                ? $", {(hi[0] - lo[0]) / 1000:0.##} × {(hi[1] - lo[1]) / 1000:0.##} m"
                : string.Empty;
            return $"{(u.Source == "link" ? "Linked" : "Imported")} {u.Kind.ToUpperInvariant()} '{u.Name}' {where}{extent}{what}{state}.";
        }

        private static List<CadLayerInfo> BuildLayers(Document doc, ImportInstance cad, View? view, bool detailed)
        {
            LayerNames layerNames = new(doc);
            Dictionary<string, Dictionary<string, int>> counts = new(StringComparer.OrdinalIgnoreCase);
            // Counted over the WHOLE file, whatever the view hides: a layer switched off in the view is
            // still in the drawing, and 'hiddenInView' says so separately.
            foreach (CadItem item in CadGeometryWalker.Walk(cad))
            {
                string? type = CadGeometryWalker.Classify(item.Object);
                if (type is null) continue;
                string layer = layerNames.Of(item.StyleId);
                if (!counts.TryGetValue(layer, out Dictionary<string, int>? byType))
                    counts[layer] = byType = new Dictionary<string, int>();
                byType[type] = byType.TryGetValue(type, out int n) ? n + 1 : 1;
            }

            List<CadLayerInfo> layers = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            ElementId solidPattern = LinePatternElement.GetSolidPatternId();

            if (cad.Category?.SubCategories is { } subcategories)
            {
                foreach (Category layer in subcategories)
                {
                    if (!seen.Add(layer.Name)) continue;
                    counts.TryGetValue(layer.Name, out Dictionary<string, int>? byType);
                    byType ??= new Dictionary<string, int>();
                    layers.Add(new CadLayerInfo
                    {
                        Name = layer.Name,
                        SubcategoryId = layer.Id.Value,
                        Color = Hex(layer.LineColor),
                        LineWeight = detailed ? Try<int?>(() => layer.GetLineWeight(GraphicsStyleType.Projection)) : null,
                        LinePattern = detailed ? PatternName(doc, layer, solidPattern) : null,
                        HiddenInView = detailed && view is not null ? Try<bool?>(() => view.GetCategoryHidden(layer.Id)) : null,
                        PrimitiveCount = byType.Values.Sum(),
                        Primitives = byType,
                    });
                }
            }

            // Geometry on a style that is no subcategory of the import (rare, but "0" and styles shared
            // with the host exist) still counts — a primitive nobody can attribute is still in the file.
            foreach ((string layer, Dictionary<string, int> byType) in counts)
            {
                if (!seen.Add(layer)) continue;
                layers.Add(new CadLayerInfo { Name = layer, PrimitiveCount = byType.Values.Sum(), Primitives = byType });
            }

            return layers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> AllLayerNames(ImportInstance cad, List<CadItem> items, LayerNames names)
        {
            if (cad.Category?.SubCategories is { } subcategories)
                foreach (Category layer in subcategories) yield return layer.Name;
            foreach (CadItem item in items) yield return names.Of(item.StyleId);
        }

        private static CadPrimitive? ToPrimitive(CadItem item, string type, string layer)
        {
            Transform t = item.Transform;
            switch (item.Object)
            {
                case GeometryInstance block:
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = new[] { Point(t.OfPoint(block.Transform.Origin)) },
                    };
                case Line line:
                {
                    Curve c = line.CreateTransformed(t);
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = new[] { Point(c.GetEndPoint(0)), Point(c.GetEndPoint(1)) },
                        Length = Mm(c.Length),
                    };
                }
                case Arc arc:
                {
                    Arc c = (Arc)arc.CreateTransformed(t);
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = c.IsBound ? new[] { Point(c.GetEndPoint(0)), Point(c.GetEndPoint(1)) } : null,
                        Center = Point(c.Center),
                        Radius = Mm(c.Radius),
                        Length = c.IsBound ? Mm(c.Length) : Mm(2 * Math.PI * c.Radius),
                    };
                }
                case Ellipse ellipse:
                {
                    Ellipse c = (Ellipse)ellipse.CreateTransformed(t);
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = Tessellate(c),
                        Center = Point(c.Center),
                        RadiusX = Mm(c.RadiusX),
                        RadiusY = Mm(c.RadiusY),
                        Length = c.IsBound ? Mm(c.Length) : null,
                    };
                }
                case Curve curve:
                {
                    Curve c = curve.CreateTransformed(t);
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = Tessellate(c),
                        Length = c.IsBound ? Mm(c.Length) : null,
                    };
                }
                case PolyLine polyLine:
                {
                    IList<XYZ> coordinates = polyLine.GetTransformed(t).GetCoordinates();
                    double length = 0;
                    for (int i = 1; i < coordinates.Count; i++) length += coordinates[i].DistanceTo(coordinates[i - 1]);
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = coordinates.Select(Point).ToArray(),
                        Length = Mm(length),
                        Closed = coordinates.Count > 2 && coordinates[0].IsAlmostEqualTo(coordinates[^1]),
                    };
                }
                case Autodesk.Revit.DB.Point point:
                    return new CadPrimitive
                    {
                        Type = type, Layer = layer, Block = item.Block,
                        Points = new[] { Point(t.OfPoint(point.Coord)) },
                    };
                case Solid solid:
                {
                    (double[]? min, double[]? max) = Extent(SolidPoints(solid).Select(t.OfPoint));
                    return new CadPrimitive { Type = type, Layer = layer, Block = item.Block, BboxMin = min, BboxMax = max };
                }
                case Mesh mesh:
                {
                    (double[]? min, double[]? max) = Extent(mesh.Vertices.Select(t.OfPoint));
                    return new CadPrimitive { Type = type, Layer = layer, Block = item.Block, BboxMin = min, BboxMax = max };
                }
                default:
                    return null;
            }
        }

        private static IEnumerable<XYZ> SolidPoints(Solid solid)
        {
            foreach (Edge edge in solid.Edges)
                foreach (XYZ p in edge.Tessellate())
                    yield return p;
        }

        private static IReadOnlyList<double[]>? Tessellate(Curve curve)
        {
            try { return curve.Tessellate().Select(Point).ToArray(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        private static string CadName(Document doc, ImportInstance cad)
        {
            // The type's name is the file name ("A-101.dwg"); the instance's own Name is often the same,
            // but a renamed or copied instance keeps the file on its type.
            string? typeName = doc.GetElement(cad.GetTypeId())?.Name;
            if (!string.IsNullOrWhiteSpace(typeName)) return typeName!;
            return cad.Category?.Name ?? cad.Name;
        }

        private static string CadKind(string pathOrName)
        {
            string ext = Path.GetExtension(pathOrName).TrimStart('.').ToLowerInvariant();
            return ext.Length is > 0 and <= 4 && ext.All(char.IsLetterOrDigit) ? ext : "cad";
        }

        private static bool IsCadKind(string kind) => kind is not ("pdf" or "image");

        private static string? CadDrawLayer(ImportInstance cad) =>
            Try<int?>(() => cad.get_Parameter(BuiltInParameter.IMPORT_BACKGROUND)?.AsInteger()) switch
            {
                1 => "background",
                0 => "foreground",
                _ => null,
            };

        private static bool IsLoaded(Document doc, ImportInstance cad)
        {
            (_, string? status) = ExternalFile(doc, doc.GetElement(cad.GetTypeId()));
            return status is null or "loaded";
        }

        private static string NotLoadedMessage(Document doc, ImportInstance cad)
        {
            (string? path, string? status) = ExternalFile(doc, doc.GetElement(cad.GetTypeId()));
            return $"'{CadName(doc, cad)}' has no geometry: the linked file is {status ?? "not loaded"}" +
                   (path is not null ? $" ({path})" : string.Empty) + ". Reload it in Manage Links.";
        }

        private static string NotCadMessage(Document doc, long id) => doc.GetElement(new ElementId(id)) switch
        {
            null => $"No element with id {id}. GetUnderlays lists the CAD imports and their ids.",
            ImageInstance => $"Element {id} is a PDF/raster image, which has no vector geometry or layers. " +
                             "Use GetPdfPageAsImage to look at it.",
            Element e => $"Element {id} is a {e.GetType().Name}, not a CAD import. GetUnderlays lists the CAD imports and their ids.",
        };

        private static string? PatternName(Document doc, Category layer, ElementId solidPattern)
        {
            ElementId? id = Try<ElementId?>(() => layer.GetLinePatternId(GraphicsStyleType.Projection));
            if (id is null || id == ElementId.InvalidElementId) return null;
            if (id == solidPattern) return "Solid";
            return doc.GetElement(id)?.Name;
        }

        // ── Images (PDF / raster) ───────────────────────────────────────────────────────────────────

        private static UnderlayInfo DescribeImage(Document doc, ImageInstance image)
        {
            ImageType? type = doc.GetElement(image.GetTypeId()) as ImageType;
            View? owner = doc.GetElement(image.OwnerViewId) as View;
            string? path = Try<string?>(() => type?.Path);
            string name = !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path)! : type?.Name ?? image.Name;
            string kind = ImageKind(doc, image);

            (double[]? min, double[]? max) = Bounds(image.get_BoundingBox(owner));

            int? pixelWidth = Positive(Try<int?>(() => type?.WidthInPixels));
            int? pixelHeight = Positive(Try<int?>(() => type?.HeightInPixels));
            double? dpi = Positive(Try<double?>(() => type?.Resolution));
            // Paper size from pixels and DPI — the definition, and independent of how Revit sizes the type.
            double? paperWidth = pixelWidth is not null && dpi is not null ? Round(pixelWidth.Value / dpi.Value * 25.4) : Mm(Try<double?>(() => type?.Width));
            double? paperHeight = pixelHeight is not null && dpi is not null ? Round(pixelHeight.Value / dpi.Value * 25.4) : Mm(Try<double?>(() => type?.Height));
            double? placedWidth = Mm(Try<double?>(() => image.Width));
            double? placedHeight = Mm(Try<double?>(() => image.Height));
            double? scale = placedWidth is > 0 && paperWidth is > 0 ? Math.Round(placedWidth.Value / paperWidth.Value, 2) : null;

            UnderlayImageInfo imageInfo = new()
            {
                PageNumber = kind == "pdf" ? Positive(Try<int?>(() => type?.PageNumber)) : null,
                ResolutionDpi = dpi,
                PaperWidthMm = paperWidth,
                PaperHeightMm = paperHeight,
                PlacedWidthMm = placedWidth,
                PlacedHeightMm = placedHeight,
                Scale = scale,
                LockProportions = Try<bool?>(() => image.LockProportions) ?? false,
            };

            UnderlayInfo info = new()
            {
                Id = image.Id.Value,
                Name = name,
                Kind = kind,
                Source = Try<ImageTypeSource?>(() => type?.Source) switch
                {
                    ImageTypeSource.Link => "link",
                    ImageTypeSource.Import => "import",
                    ImageTypeSource.Internal => "internal",
                    _ => "import",
                },
                FilePath = string.IsNullOrWhiteSpace(path) ? null : path,
                FileStatus = Try<ImageTypeStatus?>(() => type?.Status) switch
                {
                    ImageTypeStatus.Loaded => "loaded",
                    ImageTypeStatus.FailedToLoad => "failedToLoad",
                    ImageTypeStatus.Unloaded => "unloaded",
                    ImageTypeStatus.Imported => "imported",
                    ImageTypeStatus.Generated => "generated",
                    _ => null,
                },
                ViewSpecific = true,
                OwnerViewId = owner?.Id.Value,
                OwnerViewName = owner?.Name,
                OwnerViewType = owner?.ViewType.ToString(),
                OwnerViewScale = ViewScale(owner),
                SheetNumber = (owner as ViewSheet)?.SheetNumber,
                Workset = WorksetName(doc, image),
                Pinned = image.Pinned,
                DrawLayer = Try<Autodesk.Revit.DB.DrawLayer?>(() => image.DrawLayer) switch
                {
                    Autodesk.Revit.DB.DrawLayer.Background => "background",
                    Autodesk.Revit.DB.DrawLayer.Foreground => "foreground",
                    _ => null,
                },
                Origin = PointOrNull(Try<XYZ?>(() => image.GetLocation(BoxPlacement.Center))),
                BboxMin = min,
                BboxMax = max,
                Image = imageInfo,
            };
            return info with { Summary = ImageSummary(info) };
        }

        private static string ImageKind(Document doc, ImageInstance image)
        {
            ImageType? type = doc.GetElement(image.GetTypeId()) as ImageType;
            string? path = Try<string?>(() => type?.Path);
            string probe = !string.IsNullOrWhiteSpace(path) ? path! : type?.Name ?? image.Name;
            return string.Equals(Path.GetExtension(probe), ".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "image";
        }

        private static string ImageSummary(UnderlayInfo u)
        {
            UnderlayImageInfo i = u.Image!;
            List<string> parts = new();
            if (i.PageNumber is { } page) parts.Add($"page {page}");
            if (i.ResolutionDpi is { } dpi) parts.Add($"{dpi:0} DPI");
            if (i.PaperWidthMm is { } w && i.PaperHeightMm is { } h) parts.Add($"{w:0} × {h:0} mm");
            if (i.Scale is { } scale) parts.Add(scale >= 1.5 ? $"placed at 1:{scale:0.#}" : "placed at paper size");
            string where = u.SheetNumber is not null ? $"on sheet {u.SheetNumber} '{u.OwnerViewName}'"
                : u.OwnerViewName is not null ? $"on view '{u.OwnerViewName}'" : "on no view";
            string state = u.FileStatus is null or "loaded" or "imported" ? string.Empty : $", file {u.FileStatus}";
            return $"{(u.Kind == "pdf" ? "PDF" : "Image")} '{u.Name}' {where}" +
                   (parts.Count > 0 ? ", " + string.Join(", ", parts) : string.Empty) +
                   $", {(u.Pinned ? "pinned" : "not pinned")}{state}.";
        }

        // ── Shared ──────────────────────────────────────────────────────────────────────────────────

        private static View? ResolveView(Document doc, ImportInstance cad, long? viewId)
        {
            if (viewId is { } id) return doc.GetElement(new ElementId(id)) as View;
            return cad.ViewSpecific ? doc.GetElement(cad.OwnerViewId) as View : null;
        }

        private static (string? Path, string? Status) ExternalFile(Document doc, Element? type)
        {
            if (type is null) return (null, null);
            try
            {
                if (!type.IsExternalFileReference()) return (null, null);
                ExternalFileReference reference = type.GetExternalFileReference();
                string? path = null;
                try { path = ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetAbsolutePath()); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* no absolute path — keep the status */ }
                string status = reference.GetLinkedFileStatus() switch
                {
                    LinkedFileStatus.Loaded => "loaded",
                    LinkedFileStatus.NotFound => "notFound",
                    LinkedFileStatus.Unloaded => "unloaded",
                    LinkedFileStatus.LocallyUnloaded => "locallyUnloaded",
                    LinkedFileStatus.InClosedWorkset => "inClosedWorkset",
                    LinkedFileStatus.Imported => "imported",
                    LinkedFileStatus.CanBeUpgraded => "canBeUpgraded",
                    _ => "invalid",
                };
                return (string.IsNullOrWhiteSpace(path) ? null : path, status);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return (null, null);
            }
        }

        private static string? WorksetName(Document doc, Element element)
        {
            if (!doc.IsWorkshared) return null;
            return Try<string?>(() => doc.GetWorksetTable().GetWorkset(element.WorksetId)?.Name);
        }

        private static int? ViewScale(View? view)
        {
            if (view is null or ViewSheet) return null;
            return Try<int?>(() => view.Scale) is > 0 and var s ? s : null;
        }

        private static string? Hex(Color? color)
        {
            if (color is null || !color.IsValid) return null;
            return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }

        private static (double[]? Min, double[]? Max) Bounds(BoundingBoxXYZ? box)
        {
            if (box is null) return (null, null);
            // A bounding box carries its own transform; for these elements it is the identity, but
            // applying it costs nothing and keeps the answer right if it is not.
            return Extent(new[] { box.Transform.OfPoint(box.Min), box.Transform.OfPoint(box.Max) });
        }

        private static (double[]? Min, double[]? Max) Extent(IEnumerable<XYZ> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;
            foreach (XYZ p in points)
            {
                any = true;
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            if (!any) return (null, null);
            return (Point(new XYZ(minX, minY, minZ)), Point(new XYZ(maxX, maxY, maxZ)));
        }

        private static double[] Point(XYZ p) => new[] { Mm(p.X)!.Value, Mm(p.Y)!.Value, Mm(p.Z)!.Value };

        private static double[]? PointOrNull(XYZ? p) => p is null ? null : Point(p);

        // 0.1 mm: finer than any drawing means, coarse enough to keep an answer of a thousand points readable.
        private static double? Mm(double? feet) => feet is null ? null : Round(feet.Value * MmPerFoot);

        private static double Round(double value) => Math.Round(value, 1);

        private static int? Positive(int? value) => value is > 0 ? value : null;

        private static double? Positive(double? value) => value is > 0 ? value : null;

        // Reading an underlay must not fail because one property of one element is unavailable (an
        // unloaded link, a parameter a file type does not have): the field is absent instead.
        private static T Try<T>(Func<T> read)
        {
            try { return read(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return default!; }
            catch (InvalidOperationException) { return default!; }
        }

        /// <summary>Layer name per graphics style id, cached: a DWG has thousands of primitives on a few
        /// dozen styles.</summary>
        private sealed class LayerNames
        {
            private readonly Document _doc;
            private readonly Dictionary<ElementId, string> _cache = new();

            public LayerNames(Document doc) => _doc = doc;

            public string Of(ElementId styleId)
            {
                if (styleId == ElementId.InvalidElementId) return NoLayer;
                if (_cache.TryGetValue(styleId, out string? name)) return name;
                name = (_doc.GetElement(styleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? NoLayer;
                _cache[styleId] = name;
                return name;
            }
        }
    }
}
