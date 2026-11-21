using AnalyseTool.Extensions.Testing;
using Autodesk.Revit.DB.Architecture;

namespace AnalyseTool.RevitTests;

/// <summary>
///     The model of the model tests on a real document: <c>Elements&lt;T&gt;()</c> collects classes Revit does not store by
///     their own name (a room is a SpatialElement to OfClass).
/// </summary>
public sealed class ModelTestAccessTests : SeededModel
{
    [Test]
    public async Task Collect_Room_GoesThroughSpatialElement()
    {
        InTransaction("Room", () => Document.Create.NewRoom(Level, new UV(5, 3)));
        var access = new RevitModelAccess(Document);

        var rooms = access.Collect(new ElementSelector([], typeof(Room))).ToList();

        using (Assert.Multiple())
        {
            await Assert.That(RevitModelAccess.StoredClass(typeof(Room))).IsEqualTo(typeof(SpatialElement));
            await Assert.That(rooms).HasSingleItem();
            await Assert.That(rooms[0]).IsTypeOf<Room>();
        }
    }

    [Test]
    public async Task Collect_Walls_ByClassAndByCategory()
    {
        var access = new RevitModelAccess(Document);

        var byClass = access.Collect(new ElementSelector([], typeof(Wall))).Count();
        var byCategory = access.Collect(new ElementSelector([BuiltInCategory.OST_Walls], null)).Count();

        using (Assert.Multiple())
        {
            await Assert.That(byClass).IsEqualTo(4);
            await Assert.That(byCategory).IsEqualTo(4);
        }
    }
}
