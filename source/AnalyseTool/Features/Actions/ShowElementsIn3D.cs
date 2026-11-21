using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Actions;

/// <summary>
///     Shows elements: isolated temporarily in the active view, or inside a section box in a 3D view (Selection box).
/// </summary>
[McpTool(Title = "Show elements in 3D")]
[Command(Description =
    "Shows the given elements (by id): isolated temporarily in the active view — a plan stays a plan, the view never changes; elements it does " +
    "not show are left out and reported (reset with Reset Temporary Hide/Isolate) — selected and zoomed to. With sectionBox: a section box 300 mm " +
    "around them cuts a 3D view (the active one when it is 3D, else {3D} or another, created when the model has none), so their surroundings " +
    "show too (Revit's Selection Box). Changes views only, not the model. Returns { ok, isolated, view, error }.")]
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

        return document.InvokeUIAsync(uiDocument => service.ShowIn3D(uiDocument, ids, request.SectionBox), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="ShowElementsIn3D" />.</summary>
/// <param name="ElementIds">The element ids to show.</param>
public sealed record ShowElementsIn3DRequest(
    [property: Description("Element ids (Revit ElementId values) to isolate in the active view.")]
    IReadOnlyList<long>? ElementIds = null,
    [property: Description("Instead of isolating, cut the 3D view with a section box around the elements (Revit's Selection Box).")]
    bool SectionBox = false);
