using AnalyseTool.Services.Elements;
using AnalyseTool.Services.SharedParameters;

namespace AnalyseTool.RevitTests;

/// <summary>
///     <c>GetViewsAndSheets</c>, <c>GetTypeParameters</c>, <c>GetWorksets</c> and the project parameter reads through
///     their services.
/// </summary>
public sealed class ViewsAndTypesTests : SeededModel
{
    [Test]
    public async Task GetViewsAndSheets_Sheet_IsListedAsASheetAndNeverAsAView()
    {
        ViewSheet sheet = null!;
        InTransaction("Sheet", () => sheet = ViewSheet.Create(Document, ElementId.InvalidElementId));

        var result = new ViewsSheetsService().GetViewsAndSheets(Document);

        using (Assert.Multiple())
        {
            await Assert.That(result.Sheets.Select(item => item.Id)).Contains(sheet.Id.Value);
            await Assert.That(result.Views.Select(item => item.Id)).DoesNotContain(sheet.Id.Value);
            // Revit's own browser pseudo-views are not views a person would list
            await Assert.That(result.Views.All(view => view.ViewType is not ("ProjectBrowser" or "SystemBrowser" or "Internal" or "Undefined"))).IsTrue();
            await Assert.That(result.Views.All(view => view.ViewType.Length > 0)).IsTrue();
        }
    }

    [Test]
    public async Task GetTypeParameters_WallType_CarriesIdsThatTellNamesakesApart()
    {
        var typeId = Walls[0].WallType.Id.Value;

        var type = new TypeAndWorksetService().GetTypeParameters(Document, [typeId]).Types.Single();

        using (Assert.Multiple())
        {
            await Assert.That(type.TypeId).IsEqualTo(typeId);
            await Assert.That(type.Parameters).IsNotEmpty();
            await Assert.That(type.Parameters.All(parameter => parameter.Id != 0)).IsTrue();
            await Assert.That(type.Parameters.Select(parameter => parameter.Id).Distinct().Count()).IsEqualTo(type.Parameters.Count);
            await Assert.That(type.Parameters.Any(parameter => parameter.BuiltInParameter == nameof(BuiltInParameter.ALL_MODEL_TYPE_NAME))).IsTrue();
        }
    }

    [Test]
    public async Task GetTypeParameters_UnknownIds_AreSkipped()
    {
        var result = new TypeAndWorksetService().GetTypeParameters(Document, [Walls[0].WallType.Id.Value, 999_999_999]);

        await Assert.That(result.Types.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetWorksets_NotWorkshared_IsEmpty()
    {
        var result = new TypeAndWorksetService().GetWorksets(Document);

        await Assert.That(result.IsWorkshared).IsFalse();
        await Assert.That(result.Worksets).IsEmpty();
    }

    [Test]
    public async Task BindingOptions_NewProject_OffersWallsAndTheDataGroup()
    {
        var options = new ProjectParameterService().BindingOptions(Document);
        var walls = Category.GetCategory(Document, BuiltInCategory.OST_Walls);

        using (Assert.Multiple())
        {
            await Assert.That(options.Categories.Select(category => category.Id)).Contains(walls.Id.Value);
            await Assert.That(options.DefaultGroup).IsEqualTo(GroupTypeId.Data.TypeId);
            await Assert.That(options.Groups.Select(group => group.TypeId)).Contains(GroupTypeId.Data.TypeId);
        }
    }

    [Test]
    public async Task ParameterReport_UnknownParameter_IsAnErrorNotAFailure()
    {
        var result = new ParameterReportService().Build(Document, [new ParameterReportRequest(Guid.NewGuid(), null)], 10);

        await Assert.That(result.Parameters.Single().Error).IsNotNull();
    }
}
