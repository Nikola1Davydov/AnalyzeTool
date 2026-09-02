using AnalyseTool.Tools.Elements;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitTests;

/// <summary>GetViewsAndSheets and GetTypeParameters through their services — the two findings of
/// the 2026-09-02 field test that a schema could not have caught.</summary>
public sealed class ViewsAndTypesTests : SeededModel
{
    [Test]
    public async Task Sheets_are_listed_as_sheets_and_never_as_views()
    {
        ViewSheet sheet = null!;
        InTransaction("sheet", () => sheet = ViewSheet.Create(Document, ElementId.InvalidElementId));

        ViewsAndSheetsResult result = new ViewsSheetsService().GetViewsAndSheets(Document);

        using (Assert.Multiple())
        {
            await Assert.That(result.Sheets.Select(s => s.Id)).Contains(sheet.Id.Value);
            await Assert.That(result.Views.Select(v => v.Id)).DoesNotContain(sheet.Id.Value);
            // Revit's own browser pseudo-views are not views a person would list either.
            await Assert.That(result.Views.All(v => v.ViewType is not ("ProjectBrowser" or "SystemBrowser" or "Internal" or "Undefined"))).IsTrue();
            await Assert.That(result.Views.All(v => v.ViewType.Length > 0)).IsTrue();
        }
    }

    [Test]
    public async Task Type_parameters_carry_ids_that_tell_namesakes_apart()
    {
        long typeId = Walls[0].WallType.Id.Value;

        TypeParametersResult result = new TypeAndWorksetService().GetTypeParameters(Document, [typeId]);

        TypeParametersInfo type = result.Types.Single();
        using (Assert.Multiple())
        {
            await Assert.That(type.TypeId).IsEqualTo(typeId);
            await Assert.That(type.Parameters).IsNotEmpty();
            await Assert.That(type.Parameters.All(p => p.Id != 0)).IsTrue();
            // Ids are unique even where names repeat (a wall type carries two "category" parameters).
            await Assert.That(type.Parameters.Select(p => p.Id).Distinct().Count()).IsEqualTo(type.Parameters.Count);
            await Assert.That(type.Parameters.Any(p => p.BuiltInParameter == nameof(BuiltInParameter.ALL_MODEL_TYPE_NAME))).IsTrue();
        }
    }

    [Test]
    public async Task Unknown_type_ids_are_skipped_rather_than_failing_the_batch()
    {
        TypeParametersResult result = new TypeAndWorksetService().GetTypeParameters(Document, [Walls[0].WallType.Id.Value, 999_999_999]);

        await Assert.That(result.Types.Count).IsEqualTo(1);
    }
}
