using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Sample;

/// <summary>
///     The command of the sample page: reads the model in batches and reports progress. Also an MCP tool.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description = "Counts the model elements of the active document by category, in batches with progress.")]
public sealed class CountElementsByCategory(ActiveDocument document) : ICommand<IReadOnlyList<CategoryCount>>
{
    private const int BatchSize = 500;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryCount>> ExecuteAsync(CommandContext context)
    {
        var ids = await document.InvokeAsync(
            revitDocument => new FilteredElementCollector(revitDocument).WhereElementIsNotElementType().ToElementIds().ToList(),
            context.CancellationToken);

        // Filled on the Revit thread, one batch after another
        var counts = new Dictionary<string, int>();
        await document.InvokeBatchedAsync(ids, BatchSize, (revitDocument, batch) =>
        {
            foreach (var id in batch)
            {
                if (revitDocument.GetElement(id)?.Category?.Name is { } category)
                {
                    counts[category] = counts.GetValueOrDefault(category) + 1;
                }
            }
        }, context.Progress, context.CancellationToken);

        return counts
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new CategoryCount(pair.Key, pair.Value))
            .ToList();
    }
}

/// <summary>An item of <see cref="CountElementsByCategory" />.</summary>
/// <param name="Category">The category name.</param>
/// <param name="Count">The number of elements.</param>
public sealed record CategoryCount(string Category, int Count);
