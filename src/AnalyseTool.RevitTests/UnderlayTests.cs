using AnalyseTool.Tools.Underlays;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitTests;

/// <summary>
/// #136 against a real Revit: the underlays are not fixtures on disk but made from the seeded model
/// itself — its floor plan exported to DWG and PDF, then linked, imported and placed back — so the test
/// knows exactly what the files contain (four walls, 10 × 6 ft) and needs nothing but Revit. What it
/// holds the reader to is what an agent relies on: every underlay is found and told apart, the layer
/// summary is not empty, geometry comes back in project coordinates with the instance's transform
/// applied, and a PDF page renders to a PNG.
/// </summary>
public sealed class UnderlayTests : SeededModel
{
    private const double MmPerFoot = 304.8;
    private string _folder = null!;
    private ViewPlan _plan = null!;

    private ViewPlan Plan()
    {
        ViewPlan plan = null!;
        InTransaction("plan", () => plan = CreatePlan(Document, Level));
        return plan;
    }

    private static ViewPlan CreatePlan(Document document, Level level)
    {
        ViewFamilyType type = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.FloorPlan);
        return ViewPlan.Create(document, type.Id, level.Id);
    }

    /// <summary>A consultant's model saved next to the DWG: its own level and plan, the DWG imported
    /// model-wide — then linked into the seeded document and moved, like a real linked RVT.</summary>
    private RevitLinkInstance LinkModelContaining(string dwgPath, XYZ moveBy)
    {
        string rvtPath = Path.Combine(_folder, "consultant.rvt");
        Document consultant = Application.NewProjectDocument(UnitSystem.Metric);
        try
        {
            using (Transaction t = new(consultant, "consultant"))
            {
                t.Start();
                ViewPlan plan = CreatePlan(consultant, Level.Create(consultant, 0));
                if (!consultant.Import(dwgPath, new DWGImportOptions { ThisViewOnly = false }, plan, out _))
                    throw new InvalidOperationException("DWG import into the consultant model failed");
                t.Commit();
            }
            consultant.SaveAs(rvtPath, new SaveAsOptions { OverwriteExistingFile = true });
        }
        finally
        {
            consultant.Close(false);
        }

        RevitLinkInstance link = null!;
        InTransaction("link rvt", () =>
        {
            LinkLoadResult loaded = RevitLinkType.Create(Document, ModelPathUtils.ConvertUserVisiblePathToModelPath(rvtPath), new RevitLinkOptions(false));
            link = RevitLinkInstance.Create(Document, loaded.ElementId);
            link.Pinned = false;
            ElementTransformUtils.MoveElement(Document, link.Id, moveBy);
        });
        return link;
    }

    /// <summary>The plan of the seeded walls as a DWG file (exports run outside a transaction).</summary>
    private string ExportDwg()
    {
        _folder = Path.Combine(Path.GetTempPath(), "AnalyseTool.RevitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _plan = Plan();
        bool exported = Document.Export(_folder, "walls", new List<ElementId> { _plan.Id }, new DWGExportOptions());
        if (!exported) throw new InvalidOperationException("DWG export failed");
        return Directory.GetFiles(_folder, "*.dwg").Single();
    }

    private string ExportPdf()
    {
        _folder ??= Path.Combine(Path.GetTempPath(), "AnalyseTool.RevitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _plan ??= Plan();
        bool exported = Document.Export(_folder, new List<ElementId> { _plan.Id }, new PDFExportOptions { FileName = "plan", Combine = true });
        if (!exported) throw new InvalidOperationException("PDF export failed");
        return Directory.GetFiles(_folder, "*.pdf").Single();
    }

    private ImportInstance LinkDwg(string path, bool link, XYZ? moveBy = null)
    {
        ImportInstance instance = null!;
        InTransaction(link ? "link dwg" : "import dwg", () =>
        {
            DWGImportOptions options = new() { ThisViewOnly = true, Unit = ImportUnit.Default };
            ElementId id;
            bool ok = link
                ? Document.Link(path, options, _plan, out id)
                : Document.Import(path, options, _plan, out id);
            if (!ok) throw new InvalidOperationException("DWG link/import failed");
            instance = (ImportInstance)Document.GetElement(id);
            if (moveBy is not null)
            {
                instance.Pinned = false;
                ElementTransformUtils.MoveElement(Document, instance.Id, moveBy);
            }
        });
        return instance;
    }

    private (ImageInstance Image, ViewSheet Sheet) PlacePdfOnSheet(string path)
    {
        ImageInstance image = null!;
        ViewSheet sheet = null!;
        InTransaction("pdf", () =>
        {
            sheet = ViewSheet.Create(Document, ElementId.InvalidElementId);
            sheet.SheetNumber = "A-201";
            using ImageTypeOptions options = new(path, false, ImageTypeSource.Link) { PageNumber = 1, Resolution = 150 };
            ImageType type = ImageType.Create(Document, options);
            image = ImageInstance.Create(Document, sheet, type.Id, new ImagePlacementOptions());
        });
        return (image, sheet);
    }

    [After(Test)]
    public void DeleteFiles()
    {
        // The document is closed by the base hook; a file it still holds is left for the OS to reap.
        try { if (_folder is not null) Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Test]
    public async Task A_linked_dwg_is_described_with_its_file_its_view_and_its_layers()
    {
        ImportInstance dwg = LinkDwg(ExportDwg(), link: true);

        UnderlaysResult result = UnderlayReader.GetUnderlays(Document);

        UnderlayInfo info = result.Underlays.Single(u => u.Id == dwg.Id.Value);
        using (Assert.Multiple())
        {
            await Assert.That(result.Units).IsEqualTo("mm");
            await Assert.That(info.Kind).IsEqualTo("dwg");
            await Assert.That(info.Source).IsEqualTo("link");
            await Assert.That(info.FileStatus).IsEqualTo("loaded");
            await Assert.That(info.FilePath).IsNotNull();
            await Assert.That(info.ViewSpecific).IsTrue();
            await Assert.That(info.OwnerViewId).IsEqualTo(_plan.Id.Value);
            await Assert.That(info.OwnerViewName).IsEqualTo(_plan.Name);
            // The walls came out as geometry on at least one layer, and the summary says so.
            await Assert.That(info.LayerCount).IsNotNull().And.IsGreaterThan(0);
            await Assert.That(info.PrimitiveCount).IsNotNull().And.IsGreaterThan(0);
            await Assert.That(info.Layers!.Sum(l => l.PrimitiveCount)).IsEqualTo(info.PrimitiveCount!.Value);
            await Assert.That(info.Summary).Contains("Linked DWG");
            await Assert.That(info.BboxMin).IsNotNull();
            // The seeded rectangle is 10 × 6 ft; its outline (plus wall thickness) is what the DWG holds.
            await Assert.That(info.BboxMax![0] - info.BboxMin![0]).IsGreaterThan(10 * MmPerFoot - 1);
        }
    }

    [Test]
    public async Task An_imported_dwg_is_told_apart_from_a_linked_one_and_kinds_filter()
    {
        string path = ExportDwg();
        ImportInstance linked = LinkDwg(path, link: true);
        ImportInstance imported = LinkDwg(path, link: false);

        UnderlaysResult all = UnderlayReader.GetUnderlays(Document, includeLayers: false);
        UnderlaysResult onPlan = UnderlayReader.GetUnderlays(Document, viewId: _plan.Id.Value, kinds: ["cad"]);
        UnderlaysResult pdfOnly = UnderlayReader.GetUnderlays(Document, kinds: ["pdf"]);

        using (Assert.Multiple())
        {
            await Assert.That(all.Underlays.Single(u => u.Id == linked.Id.Value).Source).IsEqualTo("link");
            await Assert.That(all.Underlays.Single(u => u.Id == imported.Id.Value).Source).IsEqualTo("import");
            // includeLayers: false skips the geometry walk and says nothing about layers.
            await Assert.That(all.Underlays.All(u => u.Layers is null)).IsTrue();
            await Assert.That(onPlan.Underlays.Select(u => u.Id)).Contains(linked.Id.Value).And.Contains(imported.Id.Value);
            await Assert.That(pdfOnly.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Geometry_comes_back_in_project_coordinates_with_the_instance_moved()
    {
        // Move the link 100 ft east: a reader that forgot the instance transform would still answer
        // coordinates around the origin.
        ImportInstance dwg = LinkDwg(ExportDwg(), link: true, moveBy: new XYZ(100, 0, 0));

        CadGeometryResult geometry = UnderlayReader.GetCadGeometry(Document, dwg.Id.Value);
        List<double> xs = geometry.Primitives.Where(p => p.Points is not null).SelectMany(p => p.Points!).Select(p => p[0]).ToList();

        using (Assert.Multiple())
        {
            await Assert.That(geometry.Error).IsNull();
            await Assert.That(geometry.Count).IsGreaterThan(0);
            await Assert.That(geometry.Returned).IsEqualTo(Math.Min(geometry.Count, UnderlayReader.DefaultGeometryLimit));
            await Assert.That(xs).IsNotEmpty();
            // The walls span x = 0…10 ft before the move; after it, every point is near 100…110 ft.
            await Assert.That(xs.Min()).IsGreaterThan(95 * MmPerFoot);
            await Assert.That(xs.Max()).IsLessThan(115 * MmPerFoot);
        }
    }

    [Test]
    public async Task Layers_and_geometry_filters_agree_and_unknown_layers_are_named()
    {
        ImportInstance dwg = LinkDwg(ExportDwg(), link: true);

        CadLayersResult layers = UnderlayReader.GetCadLayers(Document, dwg.Id.Value);
        CadLayerInfo busiest = layers.Layers.OrderByDescending(l => l.PrimitiveCount).First();
        CadGeometryResult onBusiest = UnderlayReader.GetCadGeometry(Document, dwg.Id.Value, layers: [busiest.Name.ToLowerInvariant()]);
        CadGeometryResult unknown = UnderlayReader.GetCadGeometry(Document, dwg.Id.Value, layers: ["NO-SUCH-LAYER"]);
        CadGeometryResult capped = UnderlayReader.GetCadGeometry(Document, dwg.Id.Value, limit: 1);

        using (Assert.Multiple())
        {
            await Assert.That(layers.Error).IsNull();
            await Assert.That(layers.ViewId).IsEqualTo(_plan.Id.Value); // the owner view of a view-specific link
            await Assert.That(layers.Layers.All(l => l.HiddenInView is not null)).IsTrue();
            // Case-insensitive layer names; the counts of the two commands are the same count.
            await Assert.That(onBusiest.Count).IsEqualTo(busiest.PrimitiveCount);
            await Assert.That(onBusiest.Primitives.All(p => string.Equals(p.Layer, busiest.Name, StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(unknown.Count).IsEqualTo(0);
            await Assert.That(unknown.UnknownLayers).IsNotNull().And.Contains("NO-SUCH-LAYER");
            await Assert.That(unknown.AvailableLayers).IsNotNull().And.Contains(busiest.Name);
            // A capped answer says it is capped.
            await Assert.That(capped.Returned).IsEqualTo(1);
            await Assert.That(capped.Count).IsGreaterThan(1);
        }
    }

    [Test]
    public async Task A_dwg_inside_a_revit_link_is_found_and_lands_in_host_coordinates()
    {
        // The consultant's DWG sits at x = 0…10 ft in THEIR model; their model is linked 200 ft east.
        RevitLinkInstance link = LinkModelContaining(ExportDwg(), new XYZ(200, 0, 0));

        UnderlaysResult all = UnderlayReader.GetUnderlays(Document);
        UnderlaysResult ownOnly = UnderlayReader.GetUnderlays(Document, includeLinks: false);
        UnderlayInfo inLink = all.Underlays.Single(u => u.LinkInstanceId == link.Id.Value);
        CadGeometryResult geometry = UnderlayReader.GetCadGeometry(Document, inLink.Id, linkInstanceId: link.Id.Value);
        CadLayersResult layers = UnderlayReader.GetCadLayers(Document, inLink.Id, linkInstanceId: link.Id.Value);
        CadGeometryResult notALink = UnderlayReader.GetCadGeometry(Document, inLink.Id, linkInstanceId: Walls[0].Id.Value);
        List<double> xs = geometry.Primitives.Where(p => p.Points is not null).SelectMany(p => p.Points!).Select(p => p[0]).ToList();

        using (Assert.Multiple())
        {
            await Assert.That(inLink.Kind).IsEqualTo("dwg");
            await Assert.That(inLink.Source).IsEqualTo("import");
            await Assert.That(inLink.LinkName).IsNotNull();
            await Assert.That(inLink.ViewSpecific).IsFalse();
            await Assert.That(inLink.PrimitiveCount).IsNotNull().And.IsGreaterThan(0);
            await Assert.That(inLink.Summary).Contains("in Revit link");
            // Placement and extent are in the HOST's coordinates — the link's move is applied.
            await Assert.That(inLink.BboxMin![0]).IsGreaterThan(195 * MmPerFoot);
            await Assert.That(ownOnly.Underlays.Any(u => u.LinkInstanceId is not null)).IsFalse();

            await Assert.That(geometry.Error).IsNull();
            await Assert.That(xs).IsNotEmpty();
            await Assert.That(xs.Min()).IsGreaterThan(195 * MmPerFoot);
            await Assert.That(xs.Max()).IsLessThan(215 * MmPerFoot);
            await Assert.That(layers.Error).IsNull();
            await Assert.That(layers.Layers.Sum(l => l.PrimitiveCount)).IsEqualTo(inLink.PrimitiveCount!.Value);
            await Assert.That(notALink.Error).IsNotNull().And.Contains("No Revit link instance");
        }
    }

    [Test]
    public async Task A_wall_is_not_a_cad_import_and_the_answer_says_what_it_is()
    {
        CadGeometryResult result = UnderlayReader.GetCadGeometry(Document, Walls[0].Id.Value);

        await Assert.That(result.Error).IsNotNull().And.Contains("not a CAD import");
    }

    [Test]
    public async Task A_pdf_on_a_sheet_is_described_and_renders_to_a_png()
    {
        (ImageInstance image, ViewSheet sheet) = PlacePdfOnSheet(ExportPdf());

        UnderlaysResult onSheet = UnderlayReader.GetUnderlays(Document, viewId: sheet.Id.Value);
        UnderlayInfo info = onSheet.Underlays.Single();
        PageImageResult png = new UnderlayImageService().Render(Document, image.Id.Value, maxPixels: 512, outputFolder: _folder);
        byte[] bytes = Convert.FromBase64String(png.Image!.Data!);

        using (Assert.Multiple())
        {
            await Assert.That(info.Id).IsEqualTo(image.Id.Value);
            await Assert.That(info.Kind).IsEqualTo("pdf");
            await Assert.That(info.Source).IsEqualTo("link");
            await Assert.That(info.SheetNumber).IsEqualTo("A-201");
            await Assert.That(info.OwnerViewType).IsEqualTo(nameof(ViewType.DrawingSheet));
            await Assert.That(info.Image!.PageNumber).IsEqualTo(1);
            await Assert.That(info.Image.ResolutionDpi).IsEqualTo(150);
            await Assert.That(info.Image.PaperWidthMm).IsNotNull().And.IsGreaterThan(0);
            await Assert.That(info.Image.Scale).IsNotNull().And.IsGreaterThan(0);
            await Assert.That(info.Summary).StartsWith("PDF");

            await Assert.That(png.Error).IsNull();
            await Assert.That(Math.Max(png.Width, png.Height)).IsLessThanOrEqualTo(512);
            await Assert.That(bytes.Take(4).ToArray()).IsEquivalentTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // ‰PNG
            await Assert.That(File.Exists(png.File)).IsTrue();
        }
    }

    [Test]
    public async Task A_cad_import_has_no_page_to_render()
    {
        ImportInstance dwg = LinkDwg(ExportDwg(), link: true);

        PageImageResult result = new UnderlayImageService().Render(Document, dwg.Id.Value, maxPixels: null);

        await Assert.That(result.Error).IsNotNull().And.Contains("GetCadGeometry");
    }
}
