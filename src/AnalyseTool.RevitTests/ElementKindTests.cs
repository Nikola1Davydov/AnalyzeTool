using AnalyseTool.Tools.Elements;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AnalyseTool.RevitTests;

/// <summary>"Is this a type" is asked of the element, not inferred from GetTypeId() == invalid: a room
/// has no type and is still an instance. The old test reported rooms, and every parameter sampled from
/// one, as type data.</summary>
public sealed class ElementKindTests : SeededModel
{
    private Room AddRoom()
    {
        Room room = null!;
        // Inside the seeded 10 × 6 ft rectangle of walls.
        InTransaction("Room", () => room = Document.Create.NewRoom(Level, new UV(5, 3)));
        return room;
    }

    [Test]
    public async Task A_room_is_not_an_element_type()
    {
        Room room = AddRoom();
        WallType wallType = (WallType)Document.GetElement(Walls[0].GetTypeId());

        using (Assert.Multiple())
        {
            await Assert.That(room.GetTypeId()).IsEqualTo(ElementId.InvalidElementId);
            await Assert.That(new DataElement(room).IsElementType).IsFalse();
            await Assert.That(new DataElement(wallType).IsElementType).IsTrue();
        }
    }

    [Test]
    public async Task Parameters_sampled_from_a_room_are_instance_parameters()
    {
        AddRoom();

        CategoryParametersResult result = new DataElementsCollectorService()
            .GetCategoryParameters(Document, new ElementQuery { BuiltInCategory = "OST_Rooms" });

        using (Assert.Multiple())
        {
            await Assert.That(result.Parameters).IsNotEmpty();
            await Assert.That(result.Parameters.Any(p => p.IsType)).IsFalse();
        }
    }
}
