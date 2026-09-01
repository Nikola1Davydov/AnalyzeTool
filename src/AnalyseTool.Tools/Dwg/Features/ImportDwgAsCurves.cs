using AnalyseTool.Sdk;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using Serilog;
using System.ComponentModel;

namespace AnalyseTool.Tools.Dwg
{
    /// <summary>
    /// The point of the whole Dwg slice: geometry from a DWG becomes NATIVE Revit curves without the
    /// file ever entering the document.
    ///
    /// What that avoids, and why it is worth a whole slice: an imported or linked DWG drags every layer,
    /// every nested block and every hatch into the view and slows regeneration down with them; exploding
    /// one leaves the project carrying `Import-*` line styles and foreign text types that no purge
    /// removes. Here the file is parsed outside Revit, the caller picks layers and types from
    /// GetDwgStructure, and only that selection is created — as ordinary detail or model curves that
    /// select, snap, take a line style and schedule like anything else drawn by hand.
    ///
    /// Three limits are structural rather than temporary, and the description states all three because a
    /// user has to know them before they run it:
    ///   - everything lands on ONE elevation (a Revit sketch plane is planar);
    ///   - only geometry with a +Z extrusion direction is converted (see <see cref="DwgCurveFactory"/>);
    ///   - text, points and block references are counted and reported, not created — each needs a
    ///     mapping decision that does not belong to a default.
    /// </summary>
    [RevitCommand(
        Description = "Converts geometry from a .dwg or .dxf file into native Revit detail or model " +
                      "curves. The file is read OUT OF PROCESS and never imported or linked, so no " +
                      "Import- line styles, text types or nested blocks enter the project. Pick layers " +
                      "and types with GetDwgStructure first. Lines, arcs, circles, ellipses and " +
                      "polylines are converted (a bulged polyline segment becomes a real arc, not " +
                      "line segments); text, points and block references are counted in skippedReasons " +
                      "instead, because each needs a mapping decision. All curves land on ONE elevation " +
                      "and only geometry with a +Z extrusion direction is converted. Give 'unit' when " +
                      "the drawing reports UNITLESS — nothing can infer it. MODIFIES the model: creates " +
                      "one element per curve, so importing a whole survey drawing is slower and heavier " +
                      "than linking it; import the few layers you need. Cost: one parse of the file " +
                      "plus one transaction.",
        InputType = typeof(ImportDwgAsCurves.Request),
        OutputType = typeof(DwgImportResult))]
    internal sealed class ImportDwgAsCurves : IRevitTask, IProgressAware
    {
        /// <summary>How often the work loop reports progress. Every element would be thousands of
        /// marshalled updates for an import that finishes in seconds.</summary>
        private const int ProgressInterval = 250;

        /// <summary>Cap on the distinct names/reasons echoed back. The counts stay exact; it is the list
        /// that is trimmed, so a drawing with 400 unmapped layers does not answer with 400 strings.</summary>
        private const int MaxReported = 25;

        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            if (string.IsNullOrWhiteSpace(req.Path))
                throw new ArgumentException("path is required — give the full path of a .dwg or .dxf file.");

            string target = (req.Target ?? "detail").Trim().ToLowerInvariant();
            if (target is not ("detail" or "model"))
                throw new ArgumentException($"target must be 'detail' or 'model', not '{req.Target}'.");

            Progress?.Report(new ProgressInfo(0, "Reading the drawing…"));

            // Outside RunInRevitAsync on purpose: this is slow file I/O in another process, and the
            // Revit thread is the single path to the model for every transport in the tool.
            DwgEntities data = await new DwgSidecarClient()
                .ReadAsync(req.Path, req.Layers, req.Types, req.Space, req.MaxEntities, req.Failsafe, ct)
                .ConfigureAwait(false);

            // The unit decides whether a 10 000-unit wall is 10 m or 10 km. An explicit request wins;
            // otherwise INSUNITS; and when the file is UNITLESS (code 0, very common) there is nothing
            // to fall back on, so the command says so instead of picking one.
            string? unit = !string.IsNullOrWhiteSpace(req.Unit) ? req.Unit!.Trim() : DwgUnits.NameForCode(data.Units.Code);
            if (unit is null)
            {
                return Failed(
                    $"The drawing does not state a usable unit (INSUNITS = {data.Units.Code}, " +
                    $"'{data.Units.Name}'). Pass 'unit' explicitly — one of: {DwgUnits.SupportedNames}.",
                    target, data);
            }

            if (!DwgUnits.TryGetFeetPerUnit(unit, out double feetPerUnit))
                return Failed($"Unknown unit '{unit}'. Use one of: {DwgUnits.SupportedNames}.", target, data);

            return await ctx.RunInRevitAsync<object?>(app => Create(app, req, data, target, unit, feetPerUnit, ct))
                .ConfigureAwait(false);
        }

        private object Create(
            Autodesk.Revit.UI.UIApplication app,
            Request req,
            DwgEntities data,
            string target,
            string unit,
            double feetPerUnit,
            CancellationToken ct)
        {
            Document doc = app.ActiveUIDocument.Document;
            View view = doc.ActiveView;

            // Detail curves have to lie in the view's own plane, which only makes sense for a view whose
            // plane is horizontal. Refusing a section or a 3D view here beats letting Revit reject every
            // single curve with the same exception 12 000 times.
            if (target == "detail" && view is not (ViewPlan or ViewDrafting))
            {
                return Failed(
                    $"Detail curves need a plan or drafting view; the active view '{view.Name}' is a " +
                    $"{view.ViewType}. Open a plan view, or use target 'model'.",
                    target, data, view.Name);
            }

            double elevation = target == "detail" ? ViewElevation(view) : 0;
            XYZ offset = Offset(req.Recenter, data, feetPerUnit);

            DwgCurveFactory factory = new(feetPerUnit, offset, elevation, doc.Application.ShortCurveTolerance);

            int created = 0;
            int lineStylesMapped = 0;
            Dictionary<string, int> skippedReasons = new(StringComparer.Ordinal);
            HashSet<string> unmappedLayers = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ElementId>? lineStyles = null;
            List<Curve> curves = new();

            using Transaction transaction = new(doc, "AnalyseTool: import DWG geometry");
            transaction.Start();
            CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);

            SketchPlane? sketchPlane = target == "model"
                ? SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation)))
                : null;

            for (int i = 0; i < data.Entities.Count; i++)
            {
                // Cancelling throws out of the loop with the transaction still uncommitted, so the
                // `using` rolls the whole import back — half a drawing is worse than none of it.
                ct.ThrowIfCancellationRequested();

                if (i % ProgressInterval == 0)
                {
                    Progress?.Report(new ProgressInfo(
                        data.Entities.Count == 0 ? 1 : (double)i / data.Entities.Count,
                        $"Creating curves… {created}"));
                }

                DwgEntity entity = data.Entities[i];
                curves.Clear();

                if (!factory.TryCreate(entity.Geometry, curves, out string? reason))
                {
                    Count(skippedReasons, reason ?? "not convertible");
                    continue;
                }

                foreach (Curve curve in curves)
                {
                    CurveElement? element = TryPlace(doc, view, sketchPlane, curve, skippedReasons);
                    if (element is null) continue;

                    created++;
                    if (!req.MapLineStyles) continue;

                    lineStyles ??= LineStylesOf(doc, element);
                    if (lineStyles.TryGetValue(entity.Layer, out ElementId styleId))
                    {
                        if (TryAssign(doc, element, styleId)) lineStylesMapped++;
                    }
                    else
                    {
                        unmappedLayers.Add(entity.Layer);
                    }
                }
            }

            transaction.Commit();
            Progress?.Report(new ProgressInfo(1, $"Created {created} curves"));

            return new DwgImportResult(
                Ok: true,
                Created: created,
                Skipped: skippedReasons.Values.Sum(),
                SkippedReasons: Trim(skippedReasons),
                Matched: data.Matched,
                Truncated: data.Truncated,
                Unit: unit,
                FeetPerUnit: feetPerUnit,
                Recentered: req.Recenter && !offset.IsZeroLength(),
                OffsetFeet: new[] { offset.X, offset.Y, offset.Z },
                Target: target,
                ViewName: view.Name,
                LineStylesMapped: lineStylesMapped,
                UnmappedLayers: unmappedLayers.Take(MaxReported).ToList(),
                Error: null,
                Warnings: failures.Warnings);
        }

        /// <summary>Creates one curve element, recording rather than throwing when Revit refuses it. A
        /// single bad curve in a drawing of 12 000 must not roll back the other 11 999.</summary>
        private static CurveElement? TryPlace(
            Document doc, View view, SketchPlane? sketchPlane, Curve curve, Dictionary<string, int> skippedReasons)
        {
            try
            {
                // Written out rather than as a conditional expression: the two branches return
                // DetailCurve and ModelCurve, which share a base class but convert to neither.
                if (sketchPlane is null) return doc.Create.NewDetailCurve(view, curve);
                return doc.Create.NewModelCurve(curve, sketchPlane);
            }
            catch (Exception e)
            {
                Count(skippedReasons, $"Revit refused the curve: {e.Message}");
                return null;
            }
        }

        /// <summary>The line styles this document offers, by name. Read from a created element rather
        /// than collected from the document: <c>GetLineStyleIds</c> is the API's own answer to "what may
        /// this element's style be set to", so every id it returns is one Revit will accept.</summary>
        private static Dictionary<string, ElementId> LineStylesOf(Document doc, CurveElement element)
        {
            Dictionary<string, ElementId> styles = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ElementId id in element.GetLineStyleIds())
                {
                    if (doc.GetElement(id) is GraphicsStyle style && !styles.ContainsKey(style.Name))
                        styles[style.Name] = id;
                }
            }
            catch (Exception e)
            {
                Log.Debug(e, "Could not read the document's line styles; DWG layers will not be mapped");
            }
            return styles;
        }

        private static bool TryAssign(Document doc, CurveElement element, ElementId styleId)
        {
            try
            {
                element.LineStyle = doc.GetElement(styleId);
                return true;
            }
            catch (Exception e)
            {
                Log.Debug(e, "Could not set the line style on an imported curve");
                return false;
            }
        }

        /// <summary>The Z the active view's plane sits at. Guarded: <c>View.Origin</c> is not defined for
        /// every view type, and an import at Z = 0 is a far better outcome than a thrown command.</summary>
        private static double ViewElevation(View view)
        {
            try
            {
                return view.Origin.Z;
            }
            catch (Exception e)
            {
                Log.Debug(e, "View {View} has no origin; importing at elevation 0", view.Name);
                return 0;
            }
        }

        /// <summary>Shift that brings the drawing's centre to the model origin. Survey drawings sit at
        /// coordinates in the hundreds of thousands, and Revit starts warning about accuracy well before
        /// that — but moving someone's geometry silently would be worse, so this is opt-in.</summary>
        private static XYZ Offset(bool recenter, DwgEntities data, double feetPerUnit)
        {
            DwgExtents? extents = data.Extents;
            if (!recenter || extents is null || extents.Min.Length < 2 || extents.Max.Length < 2) return XYZ.Zero;

            double centerX = (extents.Min[0] + extents.Max[0]) / 2 * feetPerUnit;
            double centerY = (extents.Min[1] + extents.Max[1]) / 2 * feetPerUnit;
            return new XYZ(-centerX, -centerY, 0);
        }

        private static void Count(Dictionary<string, int> counts, string reason) =>
            counts[reason] = counts.TryGetValue(reason, out int n) ? n + 1 : 1;

        private static IReadOnlyDictionary<string, int> Trim(Dictionary<string, int> counts) =>
            counts.OrderByDescending(pair => pair.Value)
                  .Take(MaxReported)
                  .ToDictionary(pair => pair.Key, pair => pair.Value);

        private static DwgImportResult Failed(string error, string target, DwgEntities data, string? viewName = null) =>
            new(Ok: false,
                Created: 0,
                Skipped: 0,
                SkippedReasons: new Dictionary<string, int>(),
                Matched: data.Matched,
                Truncated: data.Truncated,
                Unit: data.Units.Name,
                FeetPerUnit: 0,
                Recentered: false,
                OffsetFeet: new[] { 0d, 0d, 0d },
                Target: target,
                ViewName: viewName,
                LineStylesMapped: 0,
                UnmappedLayers: Array.Empty<string>(),
                Error: error,
                Warnings: Array.Empty<TransactionWarning>());

        public sealed class Request
        {
            [Description("Full path of the .dwg or .dxf file to convert.")]
            public string Path { get; set; } = string.Empty;

            [Description("Layer names to import, matched case-insensitively. Omit or leave empty to " +
                         "import every layer — rarely what you want; pick from GetDwgStructure instead.")]
            public IReadOnlyList<string>? Layers { get; set; }

            [Description("DXF type names to import, e.g. [\"LINE\",\"ARC\",\"LWPOLYLINE\"]. Omit for " +
                         "every convertible type.")]
            public IReadOnlyList<string>? Types { get; set; }

            [Description("Which part of the drawing to import: 'model' (default), 'paper' or 'all'.")]
            public string? Space { get; set; }

            [Description("Drawing unit, overriding what the file states. REQUIRED when the drawing is " +
                         "UNITLESS. One of: millimeters, centimeters, decimeters, meters, kilometers, " +
                         "inches, feet, yards, miles.")]
            public string? Unit { get; set; }

            [Description("'detail' (default) creates view-specific detail curves in the active plan or " +
                         "drafting view; 'model' creates model curves at elevation 0, visible in every view.")]
            public string? Target { get; set; }

            [Description("Move the imported geometry so the drawing's centre lands on the model origin. " +
                         "Off by default — silently relocating someone's geometry is worse than a " +
                         "far-from-origin warning — but survey drawings usually need it.")]
            public bool Recenter { get; set; }

            [Description("Give each curve the line style whose name matches its DWG layer, when the " +
                         "project has one. On by default; layers with no matching style are listed in " +
                         "unmappedLayers and keep the default style.")]
            public bool MapLineStyles { get; set; } = true;

            [Description("Cap on entities read before conversion. Defaults to 20000, capped at 200000. " +
                         "A truncated result means only a prefix of the drawing was imported.")]
            public int? MaxEntities { get; set; }

            [Description("Error-tolerant parsing: collect diagnostics instead of failing on the first " +
                         "unreadable object.")]
            public bool Failsafe { get; set; }
        }
    }
}
