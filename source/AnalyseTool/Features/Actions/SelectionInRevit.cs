using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Actions;

/// <summary>
///     Sets the selection of the active document, without zooming — the 1.x selection command the pages use.
/// </summary>
[McpTool]
[Command(Description =
    "Selects the given elements (by id) in the active document. An empty list clears the selection. Returns " +
    "{ ok, selected, ignoredIds, error } — 'selected' is what Revit ended up holding, 'ignoredIds' the requested ids " +
    "that do not exist in the document. Changes the UI selection, not the model. Cheap.")]
public sealed class SelectionInRevit(ActiveDocument document) : ICommand<SelectionInRevitRequest, SelectionResult>
{
    /// <inheritdoc />
    public Task<SelectionResult> ExecuteAsync(SelectionInRevitRequest request, CommandContext context)
    {
        var ids = (request.ElementIds ?? []).Select(id => new ElementId(id)).ToList();

        return document.InvokeUIAsync(uiDocument =>
        {
            uiDocument.Selection.SetElementIds(ids);

            // Read back: the request is what was asked for, the selection is what happened
            var held = uiDocument.Selection.GetElementIds();
            var ignored = ids.Where(id => !held.Contains(id)).Select(id => id.Value).ToList();
            return new SelectionResult(true, held.Count, null, ignored.Count == 0 ? null : ignored);
        }, context.CancellationToken);
    }
}

/// <summary>The input of <see cref="SelectionInRevit" />.</summary>
/// <param name="ElementIds">The element ids to select; empty clears the selection.</param>
public sealed record SelectionInRevitRequest(
    [property: Description("Element ids (Revit ElementId values) to select in the active document; empty clears the selection.")]
    IReadOnlyList<long>? ElementIds = null);
