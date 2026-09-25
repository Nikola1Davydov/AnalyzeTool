using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace AnalyseTool.Benchmarks;

/// <summary>
/// A project seeded in code, big enough for a measurement to mean something: <c>wallCount</c> free-standing
/// walls on one level, spread over <see cref="WallTypeCount"/> wall types (a real category has thousands of
/// instances between a handful of types — the shape DataElementsCollectorService's type cache is built for),
/// every wall carrying a Comments value and a value in one bound shared parameter.
///
/// The same idea as SeededModel in AnalyseTool.RevitTests — built in code, so every run starts from an exact
/// known state — only scaled up. Walls sit on a grid with gaps between them so no joins are computed.
/// </summary>
internal sealed class BenchmarkModel
{
    public const int WallTypeCount = 10;
    public const string SharedParameterName = "AT Benchmark Text";

    private BenchmarkModel(Document document, IReadOnlyList<Wall> walls, ElementId sharedParameterId)
    {
        Document = document;
        Walls = walls;
        SharedParameterId = sharedParameterId;
    }

    public Document Document { get; }
    public IReadOnlyList<Wall> Walls { get; }

    /// <summary>The SharedParameterElement id — what ParameterWriteService receives for a non-built-in parameter.</summary>
    public ElementId SharedParameterId { get; }

    /// <summary>The localised name of the walls category ("Walls", "Wände") — what the table command is called with.</summary>
    public string WallsCategoryName => Category.GetCategory(Document, BuiltInCategory.OST_Walls).Name;

    public static BenchmarkModel Create(Application application, int wallCount)
    {
        Document document = application.NewProjectDocument(UnitSystem.Metric);

        // The shared parameter goes in first, in its own transaction: a binding added after the walls
        // exist would still reach them, but this keeps the seed as close as possible to a real project.
        ElementId sharedParameterId = BindSharedTextParameter(application, document);

        List<Wall> walls = new(wallCount);
        using Transaction transaction = new(document, "Seed benchmark model");
        transaction.Start();

        Level level = Level.Create(document, 0);
        List<ElementId> wallTypeIds = CreateWallTypes(document);

        // Internal units are feet: 10 ft walls, 20 ft apart, 100 per row.
        const double length = 10, spacing = 20, height = 10;
        for (int i = 0; i < wallCount; i++)
        {
            double x = i % 100 * spacing;
            double y = i / 100 * spacing;
            Line line = Line.CreateBound(new XYZ(x, y, 0), new XYZ(x + length, y, 0));
            Wall wall = Wall.Create(document, line, wallTypeIds[i % wallTypeIds.Count], level.Id, height, 0, false, false);

            wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set($"Wall {i}");
            wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set($"W-{i:D5}");
            walls.Add(wall);
        }

        transaction.Commit();

        using Transaction values = new(document, "Seed shared parameter values");
        values.Start();
        Definition definition = ((SharedParameterElement)document.GetElement(sharedParameterId)).GetDefinition();
        foreach (Wall wall in walls)
            wall.get_Parameter(definition).Set("seed");
        values.Commit();

        return new BenchmarkModel(document, walls, sharedParameterId);
    }

    public void Close() => Document.Close(false);

    private static List<ElementId> CreateWallTypes(Document document)
    {
        // The type Wall.Create(doc, line, level, structural) would pick — the one the in-Revit tests use.
        WallType seed = (WallType)document.GetElement(document.GetDefaultElementTypeId(ElementTypeGroup.WallType));

        List<ElementId> ids = [seed.Id];
        for (int i = 1; i < WallTypeCount; i++)
            ids.Add(seed.Duplicate($"Benchmark wall {i}").Id);
        return ids;
    }

    /// <summary>
    /// A text shared parameter bound to walls, created through a throw-away shared parameter file. The
    /// application's SharedParametersFilename is restored afterwards: the benchmark process owns its Revit,
    /// but nothing here should depend on that.
    /// </summary>
    private static ElementId BindSharedTextParameter(Application application, Document document)
    {
        string previousFile = application.SharedParametersFilename;
        string file = Path.Combine(Path.GetTempPath(), $"AnalyseTool.Benchmarks.{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, string.Empty);

        try
        {
            application.SharedParametersFilename = file;
            DefinitionFile definitionFile = application.OpenSharedParameterFile();
            DefinitionGroup group = definitionFile.Groups.Create("AnalyseTool.Benchmarks");
            ExternalDefinition definition = (ExternalDefinition)group.Definitions.Create(
                new ExternalDefinitionCreationOptions(SharedParameterName, SpecTypeId.String.Text));

            CategorySet categories = application.Create.NewCategorySet();
            categories.Insert(Category.GetCategory(document, BuiltInCategory.OST_Walls));

            using Transaction transaction = new(document, "Bind benchmark shared parameter");
            transaction.Start();
            document.ParameterBindings.Insert(definition, application.Create.NewInstanceBinding(categories), GroupTypeId.Data);
            transaction.Commit();

            return SharedParameterElement.Lookup(document, definition.GUID).Id;
        }
        finally
        {
            if (!string.IsNullOrEmpty(previousFile)) application.SharedParametersFilename = previousFile;
            try { File.Delete(file); }
            catch (IOException) { /* a temp file left behind is not worth failing the run for */ }
        }
    }
}
