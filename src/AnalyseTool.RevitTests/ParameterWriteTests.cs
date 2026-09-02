using AnalyseTool.Tools.Actions;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitTests;

/// <summary>SetDataToParameters through ParameterWriteService — including the batch behaviour the
/// field test asked for: one bad item is reported, the others land.</summary>
public sealed class ParameterWriteTests : SeededModel
{
    private const long Comments = (long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS;
    private const long TopConstraint = (long)BuiltInParameter.WALL_HEIGHT_TYPE; // an ElementId parameter

    private readonly ParameterWriteService _service = new();

    private string CommentOf(Wall wall) => wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() ?? string.Empty;

    [Test]
    public async Task Overwrite_writes_and_counts()
    {
        SetDataResult result = _service.Write(Document,
        [
            new(Walls[0].Id.Value, Comments, "one"),
            new(Walls[1].Id.Value, Comments, "two"),
            new(999_999_999, Comments, "nobody"),
        ], ParameterWriteService.Mode.Overwrite);

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
    public async Task OnlyIfEmpty_and_SkipIfEqual_leave_existing_values_alone()
    {
        _service.Write(Document, [new(Walls[0].Id.Value, Comments, "kept")], ParameterWriteService.Mode.Overwrite);

        SetDataResult onlyIfEmpty = _service.Write(Document, [new(Walls[0].Id.Value, Comments, "new")], ParameterWriteService.Mode.OnlyIfEmpty);
        SetDataResult skipIfEqual = _service.Write(Document, [new(Walls[0].Id.Value, Comments, "kept")], ParameterWriteService.Mode.SkipIfEqual);

        using (Assert.Multiple())
        {
            await Assert.That(onlyIfEmpty.Written).IsEqualTo(0);
            await Assert.That(onlyIfEmpty.Skipped).IsEqualTo(1);
            await Assert.That(skipIfEqual.Written).IsEqualTo(0);
            await Assert.That(CommentOf(Walls[0])).IsEqualTo("kept");
        }
    }

    [Test]
    public async Task One_unconvertible_item_is_reported_and_the_rest_are_committed()
    {
        // A string into an ElementId parameter: the very call that used to kill the whole batch
        // (field test 2026-09-02, B3) — and roll back the good writes with it.
        SetDataResult result = _service.Write(Document,
        [
            new(Walls[0].Id.Value, Comments, "before the bad one"),
            new(Walls[1].Id.Value, TopConstraint, "MCP-Test"),
            new(Walls[2].Id.Value, Comments, "after the bad one"),
        ], ParameterWriteService.Mode.Overwrite);

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
    public async Task A_read_only_parameter_is_skipped_silently()
    {
        long length = (long)BuiltInParameter.CURVE_ELEM_LENGTH;

        SetDataResult result = _service.Write(Document, [new(Walls[0].Id.Value, length, "1")], ParameterWriteService.Mode.Overwrite);

        await Assert.That(result.Written).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
    }
}
