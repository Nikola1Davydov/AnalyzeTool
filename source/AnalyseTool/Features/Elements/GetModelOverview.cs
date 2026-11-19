using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The first call of a session: what an agent would otherwise guess — the language of the category names, the display
///     units, the levels and where the model has instances.
/// </summary>
[McpTool(ReadOnly = true, Title = "Model overview")]
[Command(Description =
    "START HERE — the cheapest way to stop guessing. One call returns the document's title, Revit version, UI language, " +
    "display units, workshared flag, active view, levels and a per-category INSTANCE count: { title, revitVersion, language, " +
    "displayUnits, isWorkshared, activeView: { name, type, levelId }, levels: [{ id, name, elevation }], categoryCounts, " +
    "categories: [{ name, builtInCategory, count }], hasTopography }. Call it first in a session: category names are " +
    "localised (on a German model 'Wände', not 'Walls') and lengths follow the model's display units, both of which every " +
    "other command depends on. Read-only. Cost: scans the model-category instances once, so it is proportional to model " +
    "size — call it once and keep the answer.")]
public sealed class GetModelOverview(ActiveDocument document, ModelOverviewService service) : ICommand<ModelOverview>
{
    /// <inheritdoc />
    public Task<ModelOverview> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.GetOverview, context.CancellationToken);
    }
}
