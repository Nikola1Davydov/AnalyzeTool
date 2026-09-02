using AnalyseTool.Tools.Elements;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitTests;

/// <summary>GetElements and GetCategoryParameters, through the service they delegate to, on a
/// document with four known walls. Compared by builtInCategory throughout — category NAMES are
/// localised and this Revit may be German.</summary>
public sealed class ElementQueryTests : SeededModel
{
    private readonly DataElementsCollectorService _service = new();

    [Test]
    public async Task Walls_by_builtInCategory_are_the_seeded_four()
    {
        ElementsResult result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });

        using (Assert.Multiple())
        {
            await Assert.That(result.Error).IsNull();
            await Assert.That(result.Count).IsEqualTo(4);
            await Assert.That(result.Returned).IsEqualTo(4);
            await Assert.That(result.Elements.Select(e => e.Id)).IsEquivalentTo(Walls.Select(w => w.Id.Value));
            // System family: a name, no family id — exactly the shape #98's schema had to allow.
            await Assert.That(result.Elements.All(e => e.FamilyName is { Length: > 0 })).IsTrue();
            await Assert.That(result.Elements.All(e => e.FamilyId is null)).IsTrue();
        }
    }

    [Test]
    public async Task Elements_carry_the_keys_that_join_them_to_the_overview()
    {
        // #113: the identifiers the answer already held and used to drop — the built-in category name
        // beside the localised one, and the level's id beside its name.
        ElementsResult result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });

        using (Assert.Multiple())
        {
            await Assert.That(result.Elements.All(e => e.BuiltInCategory == "OST_Walls")).IsTrue();
            await Assert.That(result.Elements.All(e => e.LevelId == Level.Id.Value)).IsTrue();
            await Assert.That(result.Elements.All(e => e.Level == Level.Name)).IsTrue();
        }
    }

    [Test]
    public async Task Category_parameters_say_what_a_number_means_and_in_which_unit()
    {
        // A wall's unconnected height is a length; a metric seed document displays lengths in millimetres —
        // and that display unit is the one every value is read and written in.
        CategoryParametersResult result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });
        CategoryParameterInfo height = result.Parameters.Single(p => p.BuiltInParameter == nameof(BuiltInParameter.WALL_USER_HEIGHT_PARAM));
        CategoryParameterInfo comments = result.Parameters.Single(p => p.BuiltInParameter == nameof(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));

        using (Assert.Multiple())
        {
            await Assert.That(height.Spec).IsEqualTo("length");
            await Assert.That(height.Unit).IsEqualTo("millimeters");
            await Assert.That(comments.Spec).IsEqualTo("string");
            await Assert.That(comments.Unit).IsNull();
        }
    }

    [Test]
    public async Task Limit_truncates_and_says_so()
    {
        ElementsResult result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls", Limit = 2 });

        await Assert.That(result.Count).IsEqualTo(4);
        await Assert.That(result.Returned).IsEqualTo(2);
    }

    [Test]
    public async Task Types_are_answered_separately_from_instances()
    {
        ElementsResult types = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls", ElementKind = "types" });

        await Assert.That(types.Elements.All(e => e.IsType)).IsTrue();
        await Assert.That(types.Elements.Select(e => e.Id)).Contains(Walls[0].WallType.Id.Value);
    }

    [Test]
    public async Task An_unknown_category_is_an_error_with_suggestions_not_an_empty_list()
    {
        // The localised name of the walls category, with its last letter missing — the miss that
        // actually happens ("Wand" for "Wände", "Wall" for "Walls").
        string walls = Category.GetCategory(Document, BuiltInCategory.OST_Walls).Name;
        string almost = walls[..^1];

        ElementsResult result = _service.GetElementSummaries(Document, new ElementQuery { Category = almost });

        using (Assert.Multiple())
        {
            await Assert.That(result.Error).IsNotNull();
            await Assert.That(result.DidYouMean ?? []).Contains(walls);
            await Assert.That(result.Elements).IsEmpty();
        }
    }

    [Test]
    public async Task Requested_parameters_come_back_by_name()
    {
        string comments = LabelUtils.GetLabelFor(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        InTransaction("comment", () => Walls[0].get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("hello"));

        ElementsResult result = _service.GetElementSummaries(Document,
            new ElementQuery { BuiltInCategory = "OST_Walls", ParameterNames = [comments] });

        ElementSummary first = result.Elements.Single(e => e.Id == Walls[0].Id.Value);
        await Assert.That(first.Parameters).IsNotNull();
        await Assert.That(first.Parameters![comments]).IsEqualTo("hello");
    }

    [Test]
    public async Task Category_parameters_carry_the_id_the_write_command_takes()
    {
        CategoryParametersResult result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });

        CategoryParameterInfo comments = result.Parameters.Single(p => p.BuiltInParameter == nameof(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
        using (Assert.Multiple())
        {
            await Assert.That(result.Error).IsNull();
            await Assert.That(result.BuiltInCategory).IsEqualTo("OST_Walls");
            await Assert.That(result.Count).IsEqualTo(result.Parameters.Count);
            await Assert.That(comments.Id).IsEqualTo((long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            await Assert.That(comments.IsReadOnly).IsFalse();
            await Assert.That(comments.IsType).IsFalse();
            // A type parameter is surfaced from the sample's type, flagged as such.
            await Assert.That(result.Parameters.Any(p => p.IsType)).IsTrue();
        }
    }

    [Test]
    public async Task Category_parameters_for_an_unknown_builtInCategory_name_is_an_error()
    {
        CategoryParametersResult result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Wals" });

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Parameters).IsEmpty();
    }
}
