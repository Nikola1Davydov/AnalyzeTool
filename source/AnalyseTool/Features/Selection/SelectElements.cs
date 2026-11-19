using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Selection;

/// <summary>
///     Selects elements in the active document and, by default, zooms the view to them — how a caller shows the user what
///     it found. Changes only the selection and the view, never the model.
/// </summary>
[McpTool(Title = "Select elements")]
[Command(Description =
    "Selects the elements with the given ids in the active Revit document, replacing the current selection, and zooms the " +
    "active view to them unless zoom is false — the way to show the user what you found. An empty list clears the selection. " +
    "Ids that are not in the document are returned as missing. Does not change the model.")]
public sealed class SelectElements(ActiveDocument document) : ICommand<SelectElementsRequest, SelectElementsResult>
{
    /// <inheritdoc />
    public Task<SelectElementsResult> ExecuteAsync(SelectElementsRequest request, CommandContext context)
    {
        return document.InvokeUIAsync(uiDocument =>
        {
            var found = new List<ElementId>();
            var missing = new List<long>();

            foreach (var value in request.Ids.Distinct())
            {
                var id = new ElementId(value);
                if (uiDocument.Document.GetElement(id) is null)
                {
                    missing.Add(value);
                }
                else
                {
                    found.Add(id);
                }
            }

            uiDocument.Selection.SetElementIds(found);
            if (request.Zoom && found.Count > 0)
            {
                uiDocument.ShowElements(found);
            }

            return new SelectElementsResult(found.Count, missing);
        }, context.CancellationToken);
    }
}

/// <summary>The input of <see cref="SelectElements" />.</summary>
/// <param name="Ids">The element ids (<see cref="ElementId.Value" />); empty clears the selection.</param>
/// <param name="Zoom">Whether to zoom the active view to the selected elements.</param>
public sealed record SelectElementsRequest(IReadOnlyList<long> Ids, bool Zoom = true);

/// <summary>The result of <see cref="SelectElements" />.</summary>
/// <param name="Selected">How many elements are selected now.</param>
/// <param name="Missing">The ids that are not in the document.</param>
public sealed record SelectElementsResult(int Selected, IReadOnlyList<long> Missing);
