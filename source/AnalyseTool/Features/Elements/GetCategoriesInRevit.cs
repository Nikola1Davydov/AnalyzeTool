using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The localised names of the model categories of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns the LOCALISED names of all model categories in the active document (on a German model 'Wände', not 'Walls'). " +
    "For commands, prefer the language-independent builtInCategory from GetModelOverview.categories — GetElements and " +
    "GetCategoryParameters take it directly and it never depends on the UI language. Read-only and cheap — it reads the " +
    "category table, not the elements.")]
public sealed class GetCategoriesInRevit(ActiveDocument document, DataElementsCollectorService service) : ICommand<IReadOnlyList<string>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync<IReadOnlyList<string>>(service.GetModelCategoriesNames, context.CancellationToken);
    }
}
