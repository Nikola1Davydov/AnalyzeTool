using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core.Executors;

namespace AnalyseTool.RevitTests;

/// <summary>
/// A fresh in-memory project per test: one level, four walls in a rectangle, closed without saving.
/// Seeded in code rather than opened from a file so every test starts from an exact known state and
/// nothing leaks between tests. Field initializers must not touch Revit (they run before Revit is
/// injected), so everything is built in the hook.
/// </summary>
public abstract class SeededModel : RevitApiTest
{
    protected Document Document { get; private set; } = null!;
    protected Level Level { get; private set; } = null!;
    protected IReadOnlyList<Wall> Walls { get; private set; } = Array.Empty<Wall>();

    [Before(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void SeedModel()
    {
        Document = Application.NewProjectDocument(UnitSystem.Metric);

        using Transaction transaction = new(Document, "Seed model");
        transaction.Start();
        Level = Level.Create(Document, 0);
        Walls =
        [
            Wall.Create(Document, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), Level.Id, false),
            Wall.Create(Document, Line.CreateBound(new XYZ(10, 0, 0), new XYZ(10, 6, 0)), Level.Id, false),
            Wall.Create(Document, Line.CreateBound(new XYZ(10, 6, 0), new XYZ(0, 6, 0)), Level.Id, false),
            Wall.Create(Document, Line.CreateBound(new XYZ(0, 6, 0), new XYZ(0, 0, 0)), Level.Id, false),
        ];
        transaction.Commit();
    }

    [After(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CloseModel()
    {
        Document?.Close(false);
    }

    /// <summary>Runs one change inside a transaction on the seeded document.</summary>
    protected void InTransaction(string name, Action action)
    {
        using Transaction transaction = new(Document, name);
        transaction.Start();
        action();
        transaction.Commit();
    }
}
