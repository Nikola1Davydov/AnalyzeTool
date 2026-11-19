using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Actions;

/// <summary>
///     Isolates elements temporarily in the active view.
/// </summary>
[McpTool]
[Command(Description =
    "Temporarily isolates the given elements (by id) in the active view (reversible temporary hide/isolate). " +
    "Pass an empty list to do nothing. Changes the ACTIVE VIEW, not the model, and is undone by resetting temporary " +
    "hide/isolate. Returns { ok, isolated, error, warnings }. Cost: one view transaction — cheap.")]
public sealed class IsolationInRevit(ActiveDocument document, ViewActionsService service) : ICommand<IsolationInRevitRequest, IsolationResult>
{
    /// <inheritdoc />
    public Task<IsolationResult> ExecuteAsync(IsolationInRevitRequest request, CommandContext context)
    {
        var ids = request.ElementIds ?? [];
        if (ids.Count == 0)
        {
            return Task.FromResult(new IsolationResult(true, 0, null, []));
        }

        return document.InvokeAsync(revitDocument => service.Isolate(revitDocument, ids), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="IsolationInRevit" />.</summary>
/// <param name="ElementIds">The element ids to isolate in the active view.</param>
public sealed record IsolationInRevitRequest(
    [property: Description("Element ids (Revit ElementId values) to isolate in the active view.")]
    IReadOnlyList<long>? ElementIds = null);
