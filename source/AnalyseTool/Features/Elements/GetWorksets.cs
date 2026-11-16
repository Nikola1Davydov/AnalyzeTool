using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The user worksets of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Lists the user worksets of the active document: { isWorkshared, worksets: [{ id, name, isOpen, isEditable, owner }] }. " +
    "Read-only and cheap — it reads the workset table, not the elements. isWorkshared=false with an empty list for a " +
    "non-workshared project.")]
public sealed class GetWorksets(ActiveDocument document, TypeAndWorksetService service) : ICommand<WorksetsResult>
{
    /// <inheritdoc />
    public Task<WorksetsResult> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.GetWorksets, context.CancellationToken);
    }
}
