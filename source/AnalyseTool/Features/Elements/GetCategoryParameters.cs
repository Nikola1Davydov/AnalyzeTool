using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The parameters available on a category, sampled from one element and its type.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns the parameters available on elements of a Revit category, sampled from a representative element: { category, " +
    "builtInCategory, count, parameters: [{ id, builtInParameter, name, storageType, spec, unit, isReadOnly, isType }], error, " +
    "didYouMean }. 'id' is what SetDataToParameters takes; 'spec' says what the value means (length, area, number, string…) " +
    "and 'unit' the document unit every value is read and written in (millimeters, feet…). Identify the category by the " +
    "language-independent builtInCategory (e.g. \"OST_Walls\", from GetModelOverview.categories) — the localised name works " +
    "too but is a guess on a German model. Read-only. Cost: samples one element instead of scanning the category.")]
public sealed class GetCategoryParameters(ActiveDocument document, DataElementsCollectorService service)
    : ICommand<GetCategoryParametersRequest, CategoryParametersResult>
{
    /// <inheritdoc />
    public Task<CategoryParametersResult> ExecuteAsync(GetCategoryParametersRequest request, CommandContext context)
    {
        var query = new ElementQuery { Category = request.Category, BuiltInCategory = request.BuiltInCategory };
        return document.InvokeAsync(revitDocument => service.GetCategoryParameters(revitDocument, query), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="GetCategoryParameters" />.</summary>
/// <param name="BuiltInCategory">The <c>BuiltInCategory</c> name; preferred.</param>
/// <param name="Category">The localised category name, when <paramref name="BuiltInCategory" /> is not given.</param>
public sealed record GetCategoryParametersRequest(
    [property: Description("Language-independent BuiltInCategory name, e.g. \"OST_Walls\" — preferred. GetModelOverview.categories lists the ones with elements.")]
    string? BuiltInCategory = null,
    [property: Description("Localised category name as shown in Revit (e.g. \"Wände\" on a German model), when builtInCategory is not given.")]
    string? Category = null);
