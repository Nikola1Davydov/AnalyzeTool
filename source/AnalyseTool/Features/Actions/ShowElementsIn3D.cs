using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Actions;

/// <summary>
///     Shows elements on their own in a 3D view: isolates them temporarily there, makes it the active view and zooms to them.
/// </summary>
[McpTool(Title = "Show elements in 3D")]
[Command(Description =
    "Shows the given elements (by id) on their own in a 3D view: the active view when it is 3D, else the default {3D} view or another 3D view " +
    "(one is created when the model has none). The elements are isolated temporarily (reset with Reset Temporary Hide/Isolate), selected, " +
    "the view becomes active and zooms to them. Changes views only, not the model. Returns { ok, isolated, view, error }.")]
public sealed class ShowElementsIn3D(ActiveDocument document, ViewActionsService service) : ICommand<ShowElementsIn3DRequest, ShowIn3DResult>
{
    /// <inheritdoc />
    public Task<ShowIn3DResult> ExecuteAsync(ShowElementsIn3DRequest request, CommandContext context)
    {
        var ids = request.ElementIds ?? [];
        if (ids.Count == 0)
        {
            return Task.FromResult(new ShowIn3DResult(false, 0, null, "No elements to show."));
        }

        return document.InvokeUIAsync(uiDocument => service.ShowIn3D(uiDocument, ids), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="ShowElementsIn3D" />.</summary>
/// <param name="ElementIds">The element ids to show.</param>
public sealed record ShowElementsIn3DRequest(
    [property: Description("Element ids (Revit ElementId values) to show on their own in a 3D view.")]
    IReadOnlyList<long>? ElementIds = null);
