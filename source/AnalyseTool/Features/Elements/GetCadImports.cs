using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The imported (not linked) CAD instances of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns imported (non-linked) CAD instances in the document: [{ id, name }]. Imported CAD is often a model-hygiene " +
    "concern. Read-only. Cost: one scan of the document's import instances.")]
public sealed class GetCadImports(ActiveDocument document, LinksService service) : ICommand<IReadOnlyList<ImportInfo>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ImportInfo>> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.GetCadImports, context.CancellationToken);
    }
}
