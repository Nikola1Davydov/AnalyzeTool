using System.IO;
using AnalyseTool.Features.Inspection;
using AnalyseTool.Hosting.Commands;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
///     What <c>InspectElement</c> reads from an element, on a level of a new project document.
/// </summary>
public sealed class ObjectInspectorTests : RevitApiTest
{
    private Document _document = null!;
    private Level _level = null!;

    [Before(Test)]
    public void CreateDocument()
    {
        _document = Application.NewProjectDocument(UnitSystem.Metric);
        _level = new FilteredElementCollector(_document).OfClass(typeof(Level)).Cast<Level>().First();
    }

    [After(Test)]
    public void CloseDocument()
    {
        var path = _document.PathName;
        _document.Close(false);

        if (path.Length > 0)
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Members_Element_ValuesReferencesAndPaths()
    {
        var inspector = new ObjectInspector(_document, limit: 20);

        var members = inspector.Members(_level, "").ToDictionary(member => member.Name);

        await Assert.That(members["Name"].Value).IsEqualTo(_level.Name);
        await Assert.That(members["Elevation"].Value).IsEqualTo(_level.Elevation);
        await Assert.That(members["Category"].Reference!.Id).IsEqualTo(_level.Category.Id.Value);
        await Assert.That(members["Geometry"].Path).IsEqualTo("Geometry");
        await Assert.That(members["Geometry"].Note).IsNotNull();
        await Assert.That(members.ContainsKey("Parameters")).IsFalse();
    }

    [Test]
    public async Task Parameters_Element_NamesWithValuesAsTheUserSeesThem()
    {
        var parameters = new ObjectInspector(_document, limit: 20).Parameters(_level);

        var elevation = parameters.Single(parameter => parameter.Name == _level.get_Parameter(BuiltInParameter.LEVEL_ELEV).Definition.Name);
        await Assert.That(elevation.Storage).IsEqualTo(nameof(StorageType.Double));
        await Assert.That(elevation.Value).IsTypeOf<string>();
    }

    [Test]
    public async Task Resolve_Path_GoesIntoTheNestedObject()
    {
        var inspector = new ObjectInspector(_document, limit: 20);

        var category = inspector.Resolve(_level, "Category");

        await Assert.That(category).IsTypeOf<Category>();
        await Assert.That(inspector.Members(category, "Category").Single(member => member.Name == "Name").Value).IsEqualTo(_level.Category.Name);
    }

    [Test]
    public async Task Resolve_UnknownProperty_IsBadRequest()
    {
        var inspector = new ObjectInspector(_document, limit: 20);

        var exception = await Assert.That(() => inspector.Resolve(_level, "Category.NoSuchThing")).Throws<CommandException>();

        await Assert.That(exception!.Code).IsEqualTo(CommandErrorCodes.BadRequest);
        await Assert.That(exception.Message).Contains("NoSuchThing");
    }

    [Test]
    public async Task Members_Collection_ItemsUpToTheLimit()
    {
        var points = new List<XYZ> { new(1, 2, 3), new(4, 5, 6), new(7, 8, 9) };

        var members = new ObjectInspector(_document, limit: 2).Members(points, "Points");

        await Assert.That(members.Select(member => member.Name)).IsEquivalentTo(["[0]", "[1]"]);
        await Assert.That((double[])members[0].Value!).IsEquivalentTo([1d, 2d, 3d]);
        await Assert.That(ObjectInspector.CountOf(points)).IsEqualTo(3);
    }
}
