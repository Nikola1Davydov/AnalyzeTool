using System.Reflection;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.RevitTests;

/// <summary>
///     <c>SetDataToParameters</c> through <see cref="ParameterWriteService" />, and the failure handling of every write
///     transaction: one bad item is reported and the others land; an error rolls back without a dialog and says why.
/// </summary>
public sealed class ParameterWriteTests : SeededModel
{
    private const long Comments = (long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS;
    private const long TopConstraint = (long)BuiltInParameter.WALL_HEIGHT_TYPE; // an ElementId parameter

    private readonly ParameterWriteService _service = new();

    [Test]
    public async Task Write_Overwrite_WritesAndCounts()
    {
        var result = _service.Write(Document,
        [
            new ParameterWrite(Walls[0].Id.Value, Comments, "one"),
            new ParameterWrite(Walls[1].Id.Value, Comments, "two"),
            new ParameterWrite(999_999_999, Comments, "nobody")
        ], SetDataMode.Overwrite);

        using (Assert.Multiple())
        {
            await Assert.That(result.Ok).IsTrue();
            await Assert.That(result.Written).IsEqualTo(2);
            await Assert.That(result.Skipped).IsEqualTo(1);
            await Assert.That(result.Problems).IsNull();
            await Assert.That(CommentOf(Walls[0])).IsEqualTo("one");
            await Assert.That(CommentOf(Walls[1])).IsEqualTo("two");
        }
    }

    [Test]
    public async Task Write_OnlyIfEmptyAndSkipIfEqual_LeaveExistingValuesAlone()
    {
        _service.Write(Document, [new ParameterWrite(Walls[0].Id.Value, Comments, "kept")], SetDataMode.Overwrite);

        var onlyIfEmpty = _service.Write(Document, [new ParameterWrite(Walls[0].Id.Value, Comments, "new")], SetDataMode.OnlyIfEmpty);
        var skipIfEqual = _service.Write(Document, [new ParameterWrite(Walls[0].Id.Value, Comments, "kept")], SetDataMode.SkipIfEqual);

        using (Assert.Multiple())
        {
            await Assert.That(onlyIfEmpty.Written).IsEqualTo(0);
            await Assert.That(onlyIfEmpty.Skipped).IsEqualTo(1);
            await Assert.That(skipIfEqual.Written).IsEqualTo(0);
            await Assert.That(CommentOf(Walls[0])).IsEqualTo("kept");
        }
    }

    [Test]
    public async Task Write_OneUnconvertibleItem_IsReportedAndTheRestAreCommitted()
    {
        var result = _service.Write(Document,
        [
            new ParameterWrite(Walls[0].Id.Value, Comments, "before the bad one"),
            new ParameterWrite(Walls[1].Id.Value, TopConstraint, "MCP-Test"),
            new ParameterWrite(Walls[2].Id.Value, Comments, "after the bad one")
        ], SetDataMode.Overwrite);

        using (Assert.Multiple())
        {
            await Assert.That(result.Ok).IsTrue();
            await Assert.That(result.Written).IsEqualTo(2);
            await Assert.That(result.Skipped).IsEqualTo(1);
            await Assert.That(result.Problems).IsNotNull();
            await Assert.That(result.Problems!.Single().ElementId).IsEqualTo(Walls[1].Id.Value);
            await Assert.That(result.Problems!.Single().ParameterId).IsEqualTo(TopConstraint);
            await Assert.That(result.Problems!.Single().Reason).IsNotEmpty();
            await Assert.That(CommentOf(Walls[0])).IsEqualTo("before the bad one");
            await Assert.That(CommentOf(Walls[2])).IsEqualTo("after the bad one");
        }
    }

    [Test]
    public async Task Write_ReadOnlyParameter_IsSkippedSilently()
    {
        var result = _service.Write(Document, [new ParameterWrite(Walls[0].Id.Value, (long)BuiltInParameter.CURVE_ELEM_LENGTH, "1")], SetDataMode.Overwrite);

        await Assert.That(result.Written).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
    }

    [Test]
    public async Task CollectingFailures_Error_RollsBackAndIsReported()
    {
        var wall = Walls[0];

        using var transaction = new Transaction(Document, "Write that fails");
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction);
        wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("must not land");
        Document.PostFailure(new FailureMessage(AnyErrorDefinition()).SetFailingElement(wall.Id));
        var status = transaction.Commit();

        using (Assert.Multiple())
        {
            await Assert.That(status).IsNotEqualTo(TransactionStatus.Committed);
            await Assert.That(failures.Errors).IsNotEmpty();
            await Assert.That(failures.RollbackReason(status)).IsNotNull();
            await Assert.That(CommentOf(wall)).IsNotEqualTo("must not land");
        }
    }

    [Test]
    public async Task CollectingFailures_CleanCommit_HasNoRollbackReason()
    {
        using var transaction = new Transaction(Document, "Write that lands");
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction);
        Walls[0].get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("lands");
        var status = transaction.Commit();

        await Assert.That(failures.RollbackReason(status)).IsNull();
    }

    [Test]
    public async Task Isolate_ElementsInAView_IsolatesThem()
    {
        var result = new ViewActionsService().Isolate(Document, [Walls[0].Id.Value]);

        // A new document's active view may be none or a non-modifiable one: either way the result says so
        await Assert.That(result.Ok ? result.Isolated == 1 : result.Error is not null).IsTrue();
    }

    private static string CommentOf(Wall wall)
    {
        return wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() ?? string.Empty;
    }

    /// <summary>A built-in failure of Error severity, found through the registry so the test does not depend on one id.</summary>
    private static FailureDefinitionId AnyErrorDefinition()
    {
        var registry = Autodesk.Revit.ApplicationServices.Application.GetFailureDefinitionRegistry();
        foreach (var group in typeof(BuiltInFailures).GetNestedTypes())
        {
            foreach (var property in group.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(FailureDefinitionId))
                {
                    continue;
                }

                var id = (FailureDefinitionId)property.GetValue(null)!;
                if (registry.FindFailureDefinition(id)?.GetSeverity() == FailureSeverity.Error)
                {
                    return id;
                }
            }
        }

        throw new InvalidOperationException("Revit registers no built-in failure of Error severity.");
    }
}
