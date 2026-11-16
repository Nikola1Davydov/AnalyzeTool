using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
///     A new project document per test: one level, four walls in a 10 × 6 ft rectangle, closed without saving. Seeded in
///     code, so every test starts from a known state and nothing leaks between tests.
/// </summary>
public abstract class SeededModel : RevitApiTest
{
    protected Document Document { get; private set; } = null!;

    protected Level Level { get; private set; } = null!;

    protected IReadOnlyList<Wall> Walls { get; private set; } = [];

    [Before(Test)]
    public void SeedModel()
    {
        Document = Application.NewProjectDocument(UnitSystem.Metric);

        using var transaction = new Transaction(Document, "Seed model");
        transaction.Start();
        Level = Level.Create(Document, 0);
        Walls =
        [
            Wall.Create(Document, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), Level.Id, false),
            Wall.Create(Document, Line.CreateBound(new XYZ(10, 0, 0), new XYZ(10, 6, 0)), Level.Id, false),
            Wall.Create(Document, Line.CreateBound(new XYZ(10, 6, 0), new XYZ(0, 6, 0)), Level.Id, false),
            Wall.Create(Document, Line.CreateBound(new XYZ(0, 6, 0), new XYZ(0, 0, 0)), Level.Id, false)
        ];
        transaction.Commit();
    }

    [After(Test)]
    public void CloseModel()
    {
        Document?.Close(false);
    }

    /// <summary>Runs one change inside a transaction on the seeded document.</summary>
    protected void InTransaction(string name, Action action)
    {
        using var transaction = new Transaction(Document, name);
        transaction.Start();
        action();
        transaction.Commit();
    }
}
