using AnalyseTool.Services.Elements;
using Autodesk.Revit.DB.Architecture;

namespace AnalyseTool.RevitTests;

/// <summary>
///     <c>GetElements</c> and <c>GetCategoryParameters</c> through the service they delegate to, on a document with four
///     known walls. Compared by <c>BuiltInCategory</c> throughout: category names are localised and this Revit may be German.
/// </summary>
public sealed class ElementQueryTests : SeededModel
{
    private readonly DataElementsCollectorService _service = new();

    [Test]
    public async Task GetElementSummaries_WallsByBuiltInCategory_AreTheSeededFour()
    {
        var result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });

        using (Assert.Multiple())
        {
            await Assert.That(result.Error).IsNull();
            await Assert.That(result.Count).IsEqualTo(4);
            await Assert.That(result.Returned).IsEqualTo(4);
            await Assert.That(result.Elements.Select(element => element.Id)).IsEquivalentTo(Walls.Select(wall => wall.Id.Value));
            // A system family: a name, no family id
            await Assert.That(result.Elements.All(element => element.FamilyName is { Length: > 0 })).IsTrue();
            await Assert.That(result.Elements.All(element => element.FamilyId is null)).IsTrue();
        }
    }

    [Test]
    public async Task GetElementSummaries_Walls_CarryTheKeysThatJoinThemToTheOverview()
    {
        var result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });

        using (Assert.Multiple())
        {
            await Assert.That(result.Elements.All(element => element.BuiltInCategory == "OST_Walls")).IsTrue();
            await Assert.That(result.Elements.All(element => element.LevelId == Level.Id.Value)).IsTrue();
            await Assert.That(result.Elements.All(element => element.Level == Level.Name)).IsTrue();
        }
    }

    [Test]
    public async Task GetElementSummaries_Limit_TruncatesAndSaysSo()
    {
        var result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls", Limit = 2 });

        await Assert.That(result.Count).IsEqualTo(4);
        await Assert.That(result.Returned).IsEqualTo(2);
    }

    [Test]
    public async Task GetElementSummaries_Types_AreAnsweredSeparatelyFromInstances()
    {
        var types = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls", ElementKind = "types" });

        await Assert.That(types.Elements.All(element => element.IsType)).IsTrue();
        await Assert.That(types.Elements.Select(element => element.Id)).Contains(Walls[0].WallType.Id.Value);
    }

    [Test]
    public async Task GetElementSummaries_UnknownCategory_IsAnErrorWithSuggestions()
    {
        // The localised name with its last letter missing — the miss that happens («Wand» for «Wände»)
        var walls = Category.GetCategory(Document, BuiltInCategory.OST_Walls).Name;

        var result = _service.GetElementSummaries(Document, new ElementQuery { Category = walls[..^1] });

        using (Assert.Multiple())
        {
            await Assert.That(result.Error).IsNotNull();
            await Assert.That(result.DidYouMean ?? []).Contains(walls);
            await Assert.That(result.Elements).IsEmpty();
        }
    }

    [Test]
    public async Task GetElementSummaries_ParameterNames_ComeBackByName()
    {
        var comments = LabelUtils.GetLabelFor(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        InTransaction("Comment", () => Walls[0].get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("hello"));

        var result = _service.GetElementSummaries(Document, new ElementQuery { BuiltInCategory = "OST_Walls", ParameterNames = [comments] });

        var first = result.Elements.Single(element => element.Id == Walls[0].Id.Value);
        await Assert.That(first.Parameters).IsNotNull();
        await Assert.That(first.Parameters![comments]).IsEqualTo("hello");
    }

    [Test]
    public async Task GetCategoryParameters_Walls_SayWhatANumberMeansAndInWhichUnit()
    {
        var result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });
        var height = result.Parameters.Single(parameter => parameter.BuiltInParameter == nameof(BuiltInParameter.WALL_USER_HEIGHT_PARAM));
        var comments = result.Parameters.Single(parameter => parameter.BuiltInParameter == nameof(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));

        using (Assert.Multiple())
        {
            await Assert.That(height.Spec).IsEqualTo("length");
            await Assert.That(height.Unit).IsEqualTo("millimeters");
            await Assert.That(comments.Spec).IsEqualTo("string");
            await Assert.That(comments.Unit).IsNull();
        }
    }

    [Test]
    public async Task GetCategoryParameters_Walls_CarryTheIdTheWriteCommandTakes()
    {
        var result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Walls" });
        var comments = result.Parameters.Single(parameter => parameter.BuiltInParameter == nameof(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));

        using (Assert.Multiple())
        {
            await Assert.That(result.Error).IsNull();
            await Assert.That(result.BuiltInCategory).IsEqualTo("OST_Walls");
            await Assert.That(result.Count).IsEqualTo(result.Parameters.Count);
            await Assert.That(comments.Id).IsEqualTo((long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            await Assert.That(comments.IsReadOnly).IsFalse();
            await Assert.That(comments.IsType).IsFalse();
            // A type parameter comes from the sample's type, flagged as such
            await Assert.That(result.Parameters.Any(parameter => parameter.IsType)).IsTrue();
        }
    }

    [Test]
    public async Task GetCategoryParameters_UnknownBuiltInCategory_IsAnError()
    {
        var result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Wals" });

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Parameters).IsEmpty();
    }

    [Test]
    public async Task DataElement_Room_IsNotAType()
    {
        var room = AddRoom();
        var wallType = (WallType)Document.GetElement(Walls[0].GetTypeId());

        using (Assert.Multiple())
        {
            await Assert.That(room.GetTypeId()).IsEqualTo(ElementId.InvalidElementId);
            await Assert.That(DataElement.Of(room).IsElementType).IsFalse();
            await Assert.That(DataElement.Of(wallType).IsElementType).IsTrue();
        }
    }

    [Test]
    public async Task GetCategoryParameters_Rooms_AreInstanceParameters()
    {
        AddRoom();

        var result = _service.GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Rooms" });

        using (Assert.Multiple())
        {
            await Assert.That(result.Parameters).IsNotEmpty();
            await Assert.That(result.Parameters.Any(parameter => parameter.IsType)).IsFalse();
        }
    }

    [Test]
    public async Task GetOverview_SeededModel_CountsTheWallsByBuiltInCategory()
    {
        var overview = new ModelOverviewService(_service).GetOverview(Document);

        using (Assert.Multiple())
        {
            await Assert.That(overview.DisplayUnits).IsEqualTo("millimeters");
            await Assert.That(overview.Categories.Single(category => category.BuiltInCategory == "OST_Walls").Count).IsEqualTo(4);
            await Assert.That(overview.Levels.Select(level => level.Id)).Contains(Level.Id.Value);
        }
    }

    private Room AddRoom()
    {
        Room room = null!;
        // Inside the seeded rectangle of walls
        InTransaction("Room", () => room = Document.Create.NewRoom(Level, new UV(5, 3)));
        return room;
    }
}
