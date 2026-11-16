using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Selection;

/// <summary>
///     The elements the user has selected in the active document — what «this», «these walls» mean in a request.
/// </summary>
[McpTool(ReadOnly = true, Title = "Selected elements")]
[Command(Description =
    "Returns the elements selected in the active Revit document: the total count and id, category and name of each. " +
    "Use it when the user refers to «the selected», «these» or «this». limit — optional, describe only the first N; " +
    "set it when the count may be large, the count stays the full one.")]
public sealed class GetSelection(ActiveDocument document) : ICommand<GetSelectionRequest, SelectionSummary>
{
    /// <inheritdoc />
    public Task<SelectionSummary> ExecuteAsync(GetSelectionRequest request, CommandContext context)
    {
        if (request.Limit is < 0)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "limit cannot be negative.");
        }

        return document.InvokeUIAsync(uiDocument =>
        {
            var ids = uiDocument.Selection.GetElementIds();
            var elements = ids
                .Take(request.Limit ?? int.MaxValue)
                .Select(id => uiDocument.Document.GetElement(id))
                .Where(element => element is not null)
                .Select(ElementSummary.Of)
                .ToList();

            return new SelectionSummary(ids.Count, elements);
        }, context.CancellationToken);
    }
}

/// <summary>The input of <see cref="GetSelection" />.</summary>
/// <param name="Limit">Describe only the first N elements; all of them when omitted.</param>
public sealed record GetSelectionRequest(int? Limit = null);

/// <summary>The result of <see cref="GetSelection" />.</summary>
/// <param name="Count">How many elements are selected.</param>
/// <param name="Elements">The selected elements, or the first <see cref="GetSelectionRequest.Limit" /> of them.</param>
public sealed record SelectionSummary(int Count, IReadOnlyList<ElementSummary> Elements);

/// <summary>An element as a caller sees it: enough to name it and to select it again.</summary>
/// <param name="Id">The element id (<see cref="ElementId.Value" />).</param>
/// <param name="Category">The category name, if the element has one.</param>
/// <param name="Name">The element name.</param>
public sealed record ElementSummary(long Id, string? Category, string Name)
{
    /// <summary>Describes an element; call it on the Revit thread.</summary>
    public static ElementSummary Of(Element element)
    {
        return new ElementSummary(element.Id.Value, element.Category?.Name, element.Name);
    }
}
