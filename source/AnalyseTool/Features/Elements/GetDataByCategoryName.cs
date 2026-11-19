using AnalyseTool.Hosting.Commands;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     Every element of a category with all its parameters — the heavy listing of the pages. Not an MCP tool: AI clients
///     use <see cref="GetElements" /> and <see cref="GetCategoryParameters" />.
/// </summary>
[Command(Description =
    "Returns all elements (instances and types) of the given Revit category in the active document, each with all its " +
    "parameters. Call GetCategoriesInRevit first to get valid category names.")]
public sealed class GetDataByCategoryName(ActiveDocument document, DataElementsCollectorService service)
    : ICommand<GetDataByCategoryNameRequest, IReadOnlyList<DataElement>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DataElement>> ExecuteAsync(GetDataByCategoryNameRequest request, CommandContext context)
    {
        if (string.IsNullOrEmpty(request.CategoryName))
        {
            return Task.FromResult<IReadOnlyList<DataElement>>([]);
        }

        return document.InvokeAsync(revitDocument => service.GetAllElementsByCategory(revitDocument, request.CategoryName), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="GetDataByCategoryName" />.</summary>
/// <param name="CategoryName">The localised category name as <c>GetCategoriesInRevit</c> returns it: «Wände», not «Walls».</param>
public sealed record GetDataByCategoryNameRequest(string CategoryName = "");
